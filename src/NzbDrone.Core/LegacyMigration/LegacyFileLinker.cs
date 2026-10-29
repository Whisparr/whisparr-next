using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private List<RootFolder> _rootFolders;
        private DateTime _rootFoldersLoadedAt;

        public LegacyFileLinker(IMediaFileService mediaFileService,
                                IMovieService movieService,
                                IDiskProvider diskProvider,
                                IRootFolderService rootFolderService,
                                IImportFileRepository importFileRepository,
                                ILegacyMigrationService migrationService)
        {
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

            var existing = _mediaFileService.FindByFullPath(path);

            if (existing != null)
            {
                return existing.MovieId == movie.Id ? LinkOutcome.AlreadyLinked : LinkOutcome.LinkedElsewhere;
            }

            var movieFolder = movie.Path.IsNullOrWhiteSpace() || !movie.Path.IsParentPath(path)
                ? Path.GetDirectoryName(path)
                : movie.Path;

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
