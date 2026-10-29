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
        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;
        private readonly IImportFileRepository _importFileRepository;
        private readonly ILegacyMigrationService _migrationService;

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

            if (_mediaFileService.GetFilesByMovie(movie.Id).Any(f => f.Path.IsNotNullOrWhiteSpace() && f.Path.PathEquals(path)))
            {
                return LinkOutcome.AlreadyLinked;
            }

            // FilterExistingFiles drops every file a MovieFile row points at, whichever movie owns it
            if (_mediaFileService.FilterExistingFiles(new List<string> { path }).Count == 0)
            {
                return LinkOutcome.LinkedElsewhere;
            }

            if (movie.Path.IsNullOrWhiteSpace() || !movie.Path.IsParentPath(path))
            {
                movie.Path = Path.GetDirectoryName(path);
                _movieService.UpdateMovie(movie);
            }

            var movieFile = new MovieFile
            {
                Path = path,
                RelativePath = movie.Path.GetRelativePath(path),
                MovieId = movie.Id,
                MatchTier = MatchTier.Confirmed,
                MediaInfo = null
            };

            fill(movieFile);

            movieFile = _mediaFileService.Add(movieFile);

            movie.MovieFileId = movieFile.Id;
            _movieService.UpdateMovie(movie);

            return LinkOutcome.Linked;
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

            var rootFolders = _rootFolderService.All();
            var rootPath = _rootFolderService.GetBestRootFolderPath(path, rootFolders);
            var root = rootFolders.FirstOrDefault(r => r.Path.PathEquals(rootPath));

            if (root == null || !root.Path.IsParentPath(path))
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, area, path, "File is outside every root folder");
                return false;
            }

            if (_importFileRepository.FindByRootFolderId(root.Id).Any(f => f.Path.PathEquals(path)))
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
