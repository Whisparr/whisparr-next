using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Extras
{
    public abstract class ImportExistingExtraFilesBase<TExtraFile> : IImportExistingExtraFiles
        where TExtraFile : ExtraFile, new()
    {
        private readonly IExtraFileService<TExtraFile> _extraFileService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;

        public ImportExistingExtraFilesBase(IExtraFileService<TExtraFile> extraFileService, IMediaFileService mediaFileService, IDiskProvider diskProvider)
        {
            _extraFileService = extraFileService;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
        }

        public abstract int Order { get; }

        /// <summary>
        /// The file an extra in an exclusive folder belongs to. The movie a scan publishes was loaded before the scan
        /// imported its video, so its MovieFileId can be stale; the tracked files are the truth.
        /// </summary>
        protected int GetCurrentMovieFileId(Movie movie)
        {
            var files = _mediaFileService.GetFilesByMovie(movie.Id);

            return files is { Count: 1 } ? files[0].Id : movie.MovieFileId;
        }

        // Whether side files need an owner lookup. Without resolved owners, the folder is only checked when there is a
        // file to assign: a studio folder can hold thousands of items.
        protected static bool IsSharedFolder(Movie movie, List<string> filesOnDisk, IReadOnlyDictionary<string, int> resolvedOwners, ISceneFolderLayout sceneFolderLayout)
        {
            return resolvedOwners != null || (filesOnDisk.Count > 0 && sceneFolderLayout.IsShared(movie));
        }

        // The owning MovieFile.Id of a side file in a shared folder, or null when the movie doesn't own it. The folder
        // pass resolves owners once (resolvedOwners); without them, the file's ownership is looked up here.
        protected static int? FindOwnerFileId(Movie movie, string path, IReadOnlyDictionary<string, int> resolvedOwners, ISidecarOwnership sidecarOwnership, List<MovieFile> folderFiles)
        {
            if (resolvedOwners != null)
            {
                return resolvedOwners.TryGetValue(path, out var id) ? id : null;
            }

            return sidecarOwnership.FindOwner(movie, path, folderFiles)?.Id;
        }

        public abstract IEnumerable<ExtraFile> ProcessFiles(Movie movie, List<string> filesOnDisk, List<string> importedFiles, string fileNameBeforeRename, IReadOnlyDictionary<string, int> resolvedOwners = null);

        public virtual ImportExistingExtraFileFilterResult<TExtraFile> FilterAndClean(Movie movie, List<string> filesOnDisk, List<string> importedFiles, bool keepExistingEntries)
        {
            var movieFiles = _extraFileService.GetFilesByMovie(movie.Id);

            if (keepExistingEntries)
            {
                var incompleteImports = movieFiles.IntersectBy(f => Path.Combine(movie.Path, f.RelativePath), filesOnDisk, i => i, PathEqualityComparer.Instance).Select(f => f.Id);

                _extraFileService.DeleteMany(incompleteImports);

                return Filter(movie, filesOnDisk, importedFiles, new List<TExtraFile>());
            }

            Clean(movie, filesOnDisk, importedFiles, movieFiles);

            return Filter(movie, filesOnDisk, importedFiles, movieFiles);
        }

        private ImportExistingExtraFileFilterResult<TExtraFile> Filter(Movie movie, List<string> filesOnDisk, List<string> importedFiles, List<TExtraFile> movieFiles)
        {
            var previouslyImported = movieFiles.IntersectBy(s => Path.Combine(movie.Path, s.RelativePath), filesOnDisk, f => f, PathEqualityComparer.Instance).ToList();
            var filteredFiles = filesOnDisk.Except(previouslyImported.Select(f => Path.Combine(movie.Path, f.RelativePath)).ToList(), PathEqualityComparer.Instance)
                                           .Except(importedFiles, PathEqualityComparer.Instance)
                                           .ToList();

            // Return files that are already imported so they aren't imported again by other importers.
            // Filter out files that were previously imported and as well as ones imported by other importers.
            return new ImportExistingExtraFileFilterResult<TExtraFile>(previouslyImported, filteredFiles);
        }

        private void Clean(Movie movie, List<string> filesOnDisk, List<string> importedFiles, List<TExtraFile> movieFiles)
        {
            var alreadyImportedFileIds = movieFiles.IntersectBy(f => Path.Combine(movie.Path, f.RelativePath), importedFiles, i => i, PathEqualityComparer.Instance)
                .Select(f => f.Id);

            var deletedFiles = movieFiles.ExceptBy(f => Path.Combine(movie.Path, f.RelativePath), filesOnDisk, i => i, PathEqualityComparer.Instance)
                .Where(f => !_diskProvider.FileExists(Path.Combine(movie.Path, f.RelativePath)))
                .Select(f => f.Id)
                .ToList();

            _extraFileService.DeleteMany(alreadyImportedFileIds);
            _extraFileService.DeleteMany(deletedFiles);
        }
    }
}
