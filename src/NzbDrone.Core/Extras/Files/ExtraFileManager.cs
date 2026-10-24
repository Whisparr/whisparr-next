using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Extras.Files
{
    public interface IManageExtraFiles
    {
        int Order { get; }
        IEnumerable<ExtraFile> CreateAfterMediaCoverUpdate(Movie movie);
        IEnumerable<ExtraFile> CreateAfterMovieScan(Movie movie, List<MovieFile> movieFiles);
        IEnumerable<ExtraFile> CreateAfterMovieImport(Movie movie, MovieFile movieFile);
        IEnumerable<ExtraFile> CreateAfterMovieFolder(Movie movie, string movieFolder);
        IEnumerable<ExtraFile> MoveFilesAfterRename(Movie movie, List<RenamedMovieFile> renamedFiles);
        bool CanImportFile(LocalMovie localMovie, MovieFile movieFile, string path, string extension, bool readOnly);
        IEnumerable<ExtraFile> ImportFiles(LocalMovie localMovie, MovieFile movieFile, List<string> files, bool isReadOnly);
    }

    public abstract class ExtraFileManager<TExtraFile> : IManageExtraFiles
        where TExtraFile : ExtraFile, new()
    {
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly Logger _logger;

        public ExtraFileManager(IConfigService configService,
                                IDiskProvider diskProvider,
                                IDiskTransferService diskTransferService,
                                Logger logger)
        {
            _configService = configService;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _logger = logger;
        }

        public abstract int Order { get; }
        public abstract IEnumerable<ExtraFile> CreateAfterMediaCoverUpdate(Movie movie);
        public abstract IEnumerable<ExtraFile> CreateAfterMovieScan(Movie movie, List<MovieFile> movieFiles);
        public abstract IEnumerable<ExtraFile> CreateAfterMovieImport(Movie movie, MovieFile movieFile);
        public abstract IEnumerable<ExtraFile> CreateAfterMovieFolder(Movie movie, string movieFolder);
        public abstract IEnumerable<ExtraFile> MoveFilesAfterRename(Movie movie, List<RenamedMovieFile> renamedFiles);
        public abstract bool CanImportFile(LocalMovie localMovie, MovieFile movieFile, string path, string extension, bool readOnly);
        public abstract IEnumerable<ExtraFile> ImportFiles(LocalMovie localMovie, MovieFile movieFile, List<string> files, bool isReadOnly);

        /// <summary>
        /// The extras that move with a renamed video: the ones linked to it, plus rows an earlier scan left without a
        /// link (null or 0) when it is the movie's only file and, in a shared folder, the file owns the sidecar.
        /// </summary>
        protected static List<TExtraFile> GetExtrasForRenamedFile(IEnumerable<TExtraFile> extraFiles, MovieFile movieFile, bool isOnlyFile, Func<TExtraFile, bool> isOwnedByFile)
        {
            var result = new List<TExtraFile>();

            foreach (var extraFile in extraFiles)
            {
                if (extraFile.MovieFileId == movieFile.Id)
                {
                    result.Add(extraFile);
                }
                else if (isOnlyFile && (extraFile.MovieFileId ?? 0) == 0 && isOwnedByFile(extraFile))
                {
                    extraFile.MovieFileId = movieFile.Id;
                    result.Add(extraFile);
                }
            }

            return result;
        }

        protected TExtraFile ImportFile(Movie movie, MovieFile movieFile, string path, bool readOnly, string extension, string fileNameSuffix = null)
        {
            var movieFilePath = Path.Combine(movie.Path, movieFile.RelativePath);
            var newFolder = Path.GetDirectoryName(movieFilePath);
            var filenameBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(movieFile.RelativePath));

            if (fileNameSuffix.IsNotNullOrWhiteSpace())
            {
                filenameBuilder.Append(fileNameSuffix);
            }

            filenameBuilder.Append(extension);

            var newFileName = Path.Combine(newFolder, filenameBuilder.ToString());

            if (newFileName == movieFilePath)
            {
                _logger.Debug("Extra file {0} not imported, due to naming interference with movie file", path);
                return null;
            }

            var transferMode = TransferMode.Move;

            if (readOnly)
            {
                transferMode = _configService.CopyUsingHardlinks ? TransferMode.HardLinkOrCopy : TransferMode.Copy;
            }

            _diskTransferService.TransferFile(path, newFileName, transferMode, true);

            return new TExtraFile
            {
                MovieId = movie.Id,
                MovieFileId = movieFile.Id,
                RelativePath = movie.Path.GetRelativePath(newFileName),
                Extension = extension
            };
        }

        /// <summary>
        /// Where an extra sat before its video was renamed. The extra's relative path is relative to the scene
        /// folder the video was in then, not to the (possibly already updated) movie path.
        /// </summary>
        protected static string GetPreviousPath(string extraRelativePath, string previousVideoPath, string previousRelativePath)
        {
            var previousVideoFolder = Path.GetDirectoryName(previousVideoPath);

            if (previousRelativePath.IsNullOrWhiteSpace())
            {
                return Path.Combine(previousVideoFolder, Path.GetFileName(extraRelativePath));
            }

            var extraFolder = Path.GetDirectoryName(extraRelativePath) ?? string.Empty;
            var previousVideoRelativeFolder = Path.GetDirectoryName(previousRelativePath) ?? string.Empty;

            // Sat next to the video
            if (string.Equals(extraFolder, previousVideoRelativeFolder, StringComparison.Ordinal))
            {
                return Path.Combine(previousVideoFolder, Path.GetFileName(extraRelativePath));
            }

            // Otherwise resolve against the old scene folder: the video's previous path minus its relative path
            var previousSceneFolder = previousVideoPath.EndsWith(previousRelativePath, StringComparison.OrdinalIgnoreCase)
                ? previousVideoPath.Substring(0, previousVideoPath.Length - previousRelativePath.Length).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : previousVideoFolder;

            return Path.Combine(previousSceneFolder, extraRelativePath);
        }

        /// <summary>
        /// A move was skipped, so the file stays where it was. Its row is relative to the (possibly changed) scene
        /// folder and must not resolve to anything else: keep it when it still points at the file, re-point it when
        /// the file sits under the new folder, otherwise queue it in <paramref name="orphaned"/> to be dropped.
        /// Nothing on disk is touched. Returns the row when it was re-pointed and needs saving.
        /// </summary>
        protected static TExtraFile KeepSkippedRowValid(Movie movie, TExtraFile extraFile, string existingFileName, List<TExtraFile> orphaned)
        {
            if (extraFile.RelativePath.IsNotNullOrWhiteSpace() &&
                Path.Combine(movie.Path, extraFile.RelativePath).PathEquals(existingFileName))
            {
                return null;
            }

            if (movie.Path.IsParentPath(existingFileName))
            {
                extraFile.RelativePath = movie.Path.GetRelativePath(existingFileName);

                return extraFile;
            }

            orphaned?.Add(extraFile);

            return null;
        }

        protected TExtraFile MoveFile(Movie movie, MovieFile movieFile, TExtraFile extraFile, string previousVideoPath, string previousRelativePath, string fileNameSuffix = null, List<TExtraFile> orphaned = null)
        {
            _logger.Trace("Renaming extra file: {0}", extraFile);

            var newFolder = Path.GetDirectoryName(Path.Combine(movie.Path, movieFile.RelativePath));
            var filenameBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(movieFile.RelativePath));

            if (fileNameSuffix.IsNotNullOrWhiteSpace())
            {
                filenameBuilder.Append(fileNameSuffix);
            }

            filenameBuilder.Append(extraFile.Extension);

            var existingFileName = GetPreviousPath(extraFile.RelativePath, previousVideoPath, previousRelativePath);
            var newFileName = Path.Combine(newFolder, filenameBuilder.ToString());

            if (newFileName.PathNotEquals(existingFileName))
            {
                if (_diskProvider.FileExists(newFileName))
                {
                    _logger.Warn("Extra file not moved, there is already a file at the destination: {0}", newFileName);

                    return KeepSkippedRowValid(movie, extraFile, existingFileName, orphaned);
                }

                try
                {
                    _logger.Trace("Renaming extra file: {0} to {1}", extraFile, newFileName);

                    _diskProvider.MoveFile(existingFileName, newFileName);
                    extraFile.RelativePath = movie.Path.GetRelativePath(newFileName);

                    _logger.Trace("Renamed extra file from: {0}", extraFile);

                    return extraFile;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to move file after rename: {0}", existingFileName);

                    return KeepSkippedRowValid(movie, extraFile, existingFileName, orphaned);
                }
            }

            return null;
        }
    }
}
