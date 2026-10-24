using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Extras.Others
{
    public class OtherExtraService : ExtraFileManager<OtherExtraFile>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly IMediaFileAttributeService _mediaFileAttributeService;
        private readonly ISceneFolderLayout _sceneFolderLayout;
        private readonly IMediaFileService _mediaFileService;
        private readonly ISidecarOwnership _sidecarOwnership;
        private readonly Logger _logger;

        public OtherExtraService(IConfigService configService,
                                 IDiskProvider diskProvider,
                                 IDiskTransferService diskTransferService,
                                 IOtherExtraFileService otherExtraFileService,
                                 IMediaFileAttributeService mediaFileAttributeService,
                                 ISceneFolderLayout sceneFolderLayout,
                                 IMediaFileService mediaFileService,
                                 ISidecarOwnership sidecarOwnership,
                                 Logger logger)
            : base(configService, diskProvider, diskTransferService, logger)
        {
            _sceneFolderLayout = sceneFolderLayout;
            _mediaFileService = mediaFileService;
            _sidecarOwnership = sidecarOwnership;
            _diskProvider = diskProvider;
            _otherExtraFileService = otherExtraFileService;
            _mediaFileAttributeService = mediaFileAttributeService;
            _logger = logger;
        }

        public override int Order => 2;

        public override IEnumerable<ExtraFile> CreateAfterMediaCoverUpdate(Movie movie)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterMovieScan(Movie movie, List<MovieFile> movieFiles)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterMovieImport(Movie movie, MovieFile movieFile)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterMovieFolder(Movie movie, string movieFolder)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> MoveFilesAfterRename(Movie movie, List<RenamedMovieFile> renamedFiles)
        {
            var extraFiles = _otherExtraFileService.GetFilesByMovie(movie.Id);
            var movedFiles = new List<OtherExtraFile>();
            var orphanedFiles = new List<OtherExtraFile>();

            var isShared = _sceneFolderLayout.IsShared(movie);
            var onlyFileId = _mediaFileService.GetFilesByMovie(movie.Id) is { Count: 1 } files ? files[0].Id : 0;
            var folderFiles = isShared ? _sidecarOwnership.GetFolderFiles(movie) : null;

            foreach (var renamedFile in renamedFiles)
            {
                var movieFile = renamedFile.MovieFile;

                bool IsOwned(OtherExtraFile e) => !isShared || _sidecarOwnership.FindOwner(movie, Path.Combine(movie.Path, e.RelativePath), folderFiles)?.Id == movieFile.Id;

                var extraFilesForMovieFile = GetExtrasForRenamedFile(extraFiles, movieFile, onlyFileId == movieFile.Id, IsOwned);

                foreach (var extraFile in extraFilesForMovieFile)
                {
                    movedFiles.AddIfNotNull(MoveFile(movie, movieFile, extraFile, renamedFile.PreviousPath, renamedFile.PreviousRelativePath, null, orphanedFiles));
                }
            }

            _otherExtraFileService.Upsert(movedFiles);
            _otherExtraFileService.DeleteMany(orphanedFiles.Select(f => f.Id));

            return movedFiles;
        }

        public override bool CanImportFile(LocalMovie localMovie, MovieFile movieFile, string path, string extension, bool readOnly)
        {
            return true;
        }

        public override IEnumerable<ExtraFile> ImportFiles(LocalMovie localMovie, MovieFile movieFile, List<string> files, bool isReadOnly)
        {
            var importedFiles = new List<ExtraFile>();
            var filteredFiles = files.Where(f => CanImportFile(localMovie, movieFile, f, Path.GetExtension(f), isReadOnly)).ToList();
            var sourcePath = localMovie.Path;
            var sourceFolder = _diskProvider.GetParentFolder(sourcePath);
            var sourceFileName = Path.GetFileNameWithoutExtension(sourcePath);
            var matchingFiles = new List<string>();
            var shared = _sceneFolderLayout.IsShared(localMovie.Movie);
            var hasNfo = false;

            foreach (var file in filteredFiles)
            {
                try
                {
                    // Filter out duplicate NFO files
                    if (file.EndsWith(".nfo", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (hasNfo)
                        {
                            continue;
                        }

                        hasNfo = true;
                    }

                    // Filename match
                    if (SidecarName.SidecarNameMatches(Path.GetFileNameWithoutExtension(file), sourceFileName))
                    {
                        matchingFiles.Add(file);
                        continue;
                    }

                    // A shared folder's sidecars are matched by name only; title and year can't tell its scenes apart
                    if (shared)
                    {
                        continue;
                    }

                    // Movie match
                    var fileMovieInfo = Parser.Parser.ParseMoviePath(file) ?? new ParsedMovieInfo();

                    if (fileMovieInfo.MovieTitle == null)
                    {
                        continue;
                    }

                    if (fileMovieInfo.MovieTitle == localMovie.FileMovieInfo.MovieTitle &&
                        fileMovieInfo.Year.Equals(localMovie.FileMovieInfo.Year))
                    {
                        matchingFiles.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import extra file: {0}", file);
                }
            }

            var copy = 1;

            foreach (var file in matchingFiles)
            {
                try
                {
                    var suffix = GetSuffix(copy, matchingFiles.Count > 1);
                    var extraFile = ImportFile(localMovie.Movie, movieFile, file, isReadOnly, Path.GetExtension(file), suffix);
                    _mediaFileAttributeService.SetFilePermissions(file);
                    _otherExtraFileService.Upsert(extraFile);
                    importedFiles.Add(extraFile);

                    copy++;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import extra file: {0}", file);
                }
            }

            return importedFiles;
        }

        private static string GetSuffix(int copy, bool multipleCopies = false)
        {
            var suffixBuilder = new StringBuilder();

            if (multipleCopies)
            {
                suffixBuilder.Append('.');
                suffixBuilder.Append(copy);
            }

            return suffixBuilder.ToString();
        }
    }
}
