using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Fingerprints.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    public class FileHashBackfillService : IExecute<GenerateOsHashesCommand>
    {
        // Upper bound on files hashed by a single run; the next scheduled run picks up the rest
        internal const int MaxFilesPerRun = 2000;

        private readonly IMediaFileService _mediaFileService;
        private readonly IImportlFileService _importFileService;
        private readonly IMovieService _movieService;
        private readonly IVideoHashService _videoHashService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public FileHashBackfillService(IMediaFileService mediaFileService,
                                       IImportlFileService importFileService,
                                       IMovieService movieService,
                                       IVideoHashService videoHashService,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            _mediaFileService = mediaFileService;
            _importFileService = importFileService;
            _movieService = movieService;
            _videoHashService = videoHashService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(GenerateOsHashesCommand message)
        {
            var movieFiles = _mediaFileService.GetFilesWithoutOsHash(MaxFilesPerRun);
            var remaining = MaxFilesPerRun - movieFiles.Count;
            var importFiles = remaining > 0 ? _importFileService.GetFilesWithoutOsHash(remaining) : new List<ImportFile>();
            var total = movieFiles.Count + importFiles.Count;

            if (total == 0)
            {
                return;
            }

            var movies = LoadMovies(movieFiles);
            var processed = 0;
            var hashedMovieFiles = new List<MovieFile>();
            var hashedImportFiles = new List<ImportFile>();

            foreach (var movieFile in movieFiles)
            {
                _logger.ProgressInfo("Hashing file {0}/{1}", ++processed, total);

                var path = ResolvePath(movieFile, movies);

                if (TryHash(path, out var osHash, out var modifiedAt))
                {
                    movieFile.OsHash = osHash;
                    movieFile.HashedFileModifiedAt = modifiedAt;
                    hashedMovieFiles.Add(movieFile);
                }
            }

            foreach (var importFile in importFiles)
            {
                _logger.ProgressInfo("Hashing file {0}/{1}", ++processed, total);

                if (TryHash(importFile.Path, out var osHash, out var modifiedAt))
                {
                    importFile.OsHash = osHash;
                    importFile.HashedFileModifiedAt = modifiedAt;
                    hashedImportFiles.Add(importFile);
                }
            }

            if (hashedMovieFiles.Count > 0)
            {
                _mediaFileService.Update(hashedMovieFiles);
            }

            if (hashedImportFiles.Count > 0)
            {
                _importFileService.UpdateMany(hashedImportFiles);
            }

            _logger.ProgressInfo("Hashed {0} of {1} files", hashedMovieFiles.Count + hashedImportFiles.Count, total);
        }

        private Dictionary<int, Movie> LoadMovies(List<MovieFile> movieFiles)
        {
            var movieIds = movieFiles.Where(f => f.MovieId > 0).Select(f => f.MovieId).Distinct().ToList();

            if (movieIds.Count == 0)
            {
                return new Dictionary<int, Movie>();
            }

            return _movieService.GetMovies(movieIds).ToDictionary(m => m.Id);
        }

        // Mapped files live under their movie's folder, unmapped ones are tracked by where they were imported from
        private static string ResolvePath(MovieFile movieFile, Dictionary<int, Movie> movies)
        {
            if (movieFile.MovieId == 0)
            {
                return movieFile.OriginalFilePath;
            }

            if (movies.TryGetValue(movieFile.MovieId, out var movie) && movie.Path != null && movieFile.RelativePath != null)
            {
                return Path.Combine(movie.Path, movieFile.RelativePath);
            }

            return null;
        }

        private bool TryHash(string path, out string osHash, out DateTime? modifiedAt)
        {
            osHash = null;
            modifiedAt = null;

            if (path == null || !_diskProvider.FileExists(path))
            {
                _logger.Debug("Skipping oshash for missing file {0}", path);

                return false;
            }

            (osHash, modifiedAt) = _videoHashService.ComputeOsHashStamp(_diskProvider, path, _logger);

            return osHash != null;
        }
    }
}
