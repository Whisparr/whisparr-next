using System.IO;
using System.Linq;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupExtraFilesInExcludedFolders : IHousekeepingTask
    {
        private readonly IOtherExtraFileRepository _extraFileRepository;
        private readonly IMovieService _movieService;
        private readonly IDiskScanService _diskScanService;

        public CleanupExtraFilesInExcludedFolders(IOtherExtraFileRepository extraFileRepository, IMovieService movieService, IDiskScanService diskScanService)
        {
            _extraFileRepository = extraFileRepository;
            _movieService = movieService;
            _diskScanService = diskScanService;
        }

        public void Clean()
        {
            // Start from the extra files: a library can hold hundreds of thousands of items and only a few extras
            var allExtraFiles = _extraFileRepository.All().ToList();

            if (allExtraFiles.Count == 0)
            {
                return;
            }

            var moviePaths = _movieService.AllMoviePaths();

            foreach (var group in allExtraFiles.GroupBy(e => e.MovieId))
            {
                // Orphaned extras are left to CleanupOrphanedExtraFiles
                if (!moviePaths.TryGetValue(group.Key, out var moviePath))
                {
                    continue;
                }

                var extraFiles = group.ToList();
                var filteredExtraFiles = _diskScanService.FilterPaths(moviePath, extraFiles.Select(e => Path.Combine(moviePath, e.RelativePath)));

                if (filteredExtraFiles.Count == extraFiles.Count)
                {
                    continue;
                }

                var excludedExtraFiles = extraFiles.Where(e => !filteredExtraFiles.Contains(Path.Combine(moviePath, e.RelativePath))).ToList();

                if (excludedExtraFiles.Any())
                {
                    _extraFileRepository.DeleteMany(excludedExtraFiles.Select(e => e.Id).ToList());
                }
            }
        }
    }
}
