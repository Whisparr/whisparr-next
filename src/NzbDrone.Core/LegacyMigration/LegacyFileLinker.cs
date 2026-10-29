using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.LegacyMigration
{
    public enum LinkOutcome
    {
        Linked,
        AlreadyLinked,
        LinkedElsewhere,
        SceneHasOtherFile,
        Missing,
        IsDirectory,
        OutsideRootFolders
    }

    public interface ILegacyFileLinker
    {
        LinkOutcome Link(Movie movie, string path, Action<MovieFile> fill);
        bool Park(string path, int qualityProfileId, string area, string reason);
    }

    // Links legacy files to scenes by writing rows only; nothing on disk is moved, renamed or deleted.
    public class LegacyFileLinker : ILegacyFileLinker
    {
        private static readonly TimeSpan RootFolderCacheTime = TimeSpan.FromSeconds(30);

        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;
        private readonly IImportFileRepository _importFileRepository;
        private readonly ILegacyMigrationService _migrationService;
        private readonly IDiskScanService _diskScanService;
        private readonly Logger _logger;

        private List<RootFolder> _rootFolders;
        private DateTime _rootFoldersLoadedAt;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public LegacyFileLinker(IMediaFileService mediaFileService,
                                IMovieService movieService,
                                IDiskProvider diskProvider,
                                IRootFolderService rootFolderService,
                                IImportFileRepository importFileRepository,
                                ILegacyMigrationService migrationService,
                                IDiskScanService diskScanService,
                                Logger logger)
        {
            _diskScanService = diskScanService;
            _logger = logger;
            _mediaFileService = mediaFileService;
            _movieService = movieService;
            _diskProvider = diskProvider;
            _rootFolderService = rootFolderService;
            _importFileRepository = importFileRepository;
            _migrationService = migrationService;
        }

        public LinkOutcome Link(Movie movie, string path, Action<MovieFile> fill)
        {
            if (_diskProvider.FolderExists(path))
            {
                return LinkOutcome.IsDirectory;
            }

            if (!_diskProvider.FileExists(path))
            {
                return LinkOutcome.Missing;
            }

            var movieFolder = movie.Path.IsNullOrWhiteSpace() || !movie.Path.IsParentPath(path)
                ? Path.GetDirectoryName(path)
                : movie.Path;
            var outcome = LinkOutcome.Linked;

            // A scan of this scene or a sibling in the same folder must not interleave with the check-then-add below
            _diskScanService.RunLocked(movie, movieFolder, () => outcome = LinkLocked(movie, path, movieFolder, fill));

            return outcome;
        }

        private LinkOutcome LinkLocked(Movie movie, string path, string movieFolder, Action<MovieFile> fill)
        {
            var existing = _mediaFileService.FindByFullPath(path);

            if (existing != null)
            {
                return ClaimExisting(movie, existing, movieFolder);
            }

            if (HasOtherFile(movie, path))
            {
                return LinkOutcome.SceneHasOtherFile;
            }

            var movieFile = new MovieFile
            {
                Path = path,
                RelativePath = movieFolder.GetRelativePath(path),
                MovieId = movie.Id,
                MatchTier = MatchTier.Confirmed,
                MediaInfo = null
            };

            fill(movieFile);

            movieFile = _mediaFileService.Add(movieFile);

            // One save covers the repoint and the file id; Add's handler wrote the movie with its old path first
            movie.Path = movieFolder;
            movie.MovieFileId = movieFile.Id;
            _movieService.UpdateMovie(movie);

            return LinkOutcome.Linked;
        }

        private LinkOutcome ClaimExisting(Movie movie, MovieFile existing, string movieFolder)
        {
            if (existing.MovieId == movie.Id)
            {
                // The disk scan may have linked it by name already; the v2 TPDB link agrees, so it is confirmed.
                // Hash, Manual and Conflict are left as they are.
                if (existing.MatchTier is MatchTier.Unknown or MatchTier.Name)
                {
                    existing.MatchTier = MatchTier.Confirmed;
                    _mediaFileService.SaveMatchTiers(new List<MovieFile> { existing });
                }

                return LinkOutcome.AlreadyLinked;
            }

            // Only a guess by name (or none) put the file on the other scene; the v2 TPDB link outranks it
            if (existing.MatchTier is not (MatchTier.Unknown or MatchTier.Name))
            {
                return LinkOutcome.LinkedElsewhere;
            }

            if (HasOtherFile(movie, existing.Path))
            {
                return LinkOutcome.SceneHasOtherFile;
            }

            MoveToMovie(movie, existing, movieFolder);

            return LinkOutcome.Linked;
        }

        // A scene keeps the file it has; a second one is the user's to place
        private bool HasOtherFile(Movie movie, string path)
        {
            if (movie.MovieFileId <= 0)
            {
                return false;
            }

            var current = _mediaFileService.GetMovie(movie.MovieFileId);

            return current != null && !current.Path.PathEquals(path);
        }

        private void MoveToMovie(Movie movie, MovieFile existing, string movieFolder)
        {
            var oldMovieId = existing.MovieId;

            existing.MovieId = movie.Id;
            existing.RelativePath = movieFolder.GetRelativePath(existing.Path);
            existing.MatchTier = MatchTier.Confirmed;
            _mediaFileService.Update(existing);

            var oldMovie = _movieService.GetMovies(new[] { oldMovieId }).FirstOrDefault();

            if (oldMovie != null && oldMovie.MovieFileId == existing.Id)
            {
                oldMovie.MovieFileId = 0;
                _movieService.UpdateMovie(oldMovie);
            }

            movie.Path = movieFolder;
            movie.MovieFileId = existing.Id;
            _movieService.UpdateMovie(movie);

            _logger.Info("Moved {0} from movie {1} to {2}: the v2 TPDB link outranks a match by name", existing.Path, oldMovieId, movie);
        }

        private List<RootFolder> GetRootFolders()
        {
            // Root folders barely change during an import, so a short cache spares a query per file
            if (_rootFolders == null || DateTime.UtcNow - _rootFoldersLoadedAt > RootFolderCacheTime)
            {
                _rootFolders = _rootFolderService.All();
                _rootFoldersLoadedAt = DateTime.UtcNow;
            }

            return _rootFolders;
        }

        public bool Park(string path, int qualityProfileId, string area, string reason)
        {
            if (_diskProvider.FolderExists(path))
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, area, path, "Path is a directory, not a file");
                return false;
            }

            if (!_diskProvider.FileExists(path))
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, area, path, "File no longer exists on disk");
                return false;
            }

            var rootFolders = GetRootFolders();
            var rootPath = _rootFolderService.GetBestRootFolderPath(path, rootFolders);
            var root = rootFolders.FirstOrDefault(r => r.Path.PathEquals(rootPath));

            if (root == null || !root.Path.IsParentPath(path))
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, area, path, "File is outside every root folder");
                return false;
            }

            if (_importFileRepository.FindByPath(path) != null)
            {
                return true;
            }

            _importFileRepository.Insert(new ImportFile
            {
                RootFolderId = root.Id,
                Path = path,
                RelativePath = root.Path.GetRelativePath(path),
                Name = Path.GetFileName(path),
                QualityProfileId = qualityProfileId
            });

            _migrationService.Report(LegacyReportCategory.UnmatchedFile, area, path, reason);
            return true;
        }
    }
}
