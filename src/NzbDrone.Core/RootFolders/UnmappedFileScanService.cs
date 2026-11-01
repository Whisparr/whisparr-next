using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders.Commands;

namespace NzbDrone.Core.RootFolders
{
    // Finds videos anywhere under the root folders that no scene or movie tracks, without touching them on disk.
    public class UnmappedFileScanService : IExecute<ScanUnmappedFilesCommand>
    {
        private readonly IRootFolderRepository _rootFolderRepository;
        private readonly IImportFileRepository _importFileRepository;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly ILegacyMigrationService _legacyMigrationService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public UnmappedFileScanService(IRootFolderRepository rootFolderRepository,
                                       IImportFileRepository importFileRepository,
                                       IMediaFileRepository mediaFileRepository,
                                       IMovieRepository movieRepository,
                                       IDiskProvider diskProvider,
                                       IDiskScanService diskScanService,
                                       ILegacyMigrationService legacyMigrationService,
                                       IManageCommandQueue commandQueueManager,
                                       Logger logger)
        {
            _rootFolderRepository = rootFolderRepository;
            _importFileRepository = importFileRepository;
            _mediaFileRepository = mediaFileRepository;
            _movieRepository = movieRepository;
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _legacyMigrationService = legacyMigrationService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(ScanUnmappedFilesCommand message)
        {
            // Files a running migration hasn't linked yet would all look untracked
            if (_legacyMigrationService.IsIncomplete())
            {
                _logger.ProgressInfo("Skipping the scan for unmapped files until the migration finishes");

                return;
            }

            var tracked = GetTrackedPaths();
            var added = 0;

            foreach (var rootFolder in _rootFolderRepository.All())
            {
                added += ScanRootFolder(rootFolder, tracked);
            }

            if (added > 0)
            {
                _commandQueueManager.Push(new GenerateOsHashesCommand(), CommandPriority.Low);
            }

            _logger.ProgressInfo("Found {0} new unmapped files", added);
        }

        private int ScanRootFolder(RootFolder rootFolder, HashSet<string> tracked)
        {
            if (!_diskProvider.FolderExists(rootFolder.Path))
            {
                _logger.Warn("Skipping root folder {0}: it can't be reached", rootFolder.Path);

                return 0;
            }

            _logger.ProgressInfo("Scanning {0} for unmapped files", rootFolder.Path);

            // Extras stay in: their folder names include "scenes" and "other", and a stray clip can be dismissed but a skipped file is never seen
            var files = _diskScanService.FilterPaths(rootFolder.Path, _diskScanService.GetVideoFiles(rootFolder.Path), false);
            var existing = _importFileRepository.FindByRootFolderId(rootFolder.Id);
            var known = new HashSet<string>(existing.Select(f => f.Path), PathEqualityComparer.Instance);

            // Something else linked these since they were listed, such as a scene's own disk scan
            var linked = existing.Where(f => tracked.Contains(f.Path)).Select(f => f.Id).ToList();

            var toAdd = files
                .Where(f => !tracked.Contains(f) && !known.Contains(f))
                .Select(f => new ImportFile
                {
                    RootFolderId = rootFolder.Id,
                    Path = f,
                    RelativePath = rootFolder.Path.GetRelativePath(f),
                    Name = Path.GetFileName(f)
                })
                .ToList();

            _importFileRepository.DeleteMany(linked);
            _importFileRepository.InsertMany(toAdd);

            return toAdd.Count;
        }

        private HashSet<string> GetTrackedPaths()
        {
            var moviePaths = _movieRepository.AllMoviePaths();
            var tracked = new HashSet<string>(PathEqualityComparer.Instance);

            foreach (var file in _mediaFileRepository.GetAllFiles())
            {
                if (file.RelativePath.IsNotNullOrWhiteSpace() && moviePaths.TryGetValue(file.MovieId, out var moviePath))
                {
                    tracked.Add(Path.Combine(moviePath, file.RelativePath));
                }
            }

            return tracked;
        }
    }
}
