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

        // Results are persisted in batches this size, so a restart mid-run keeps progress
        internal const int FlushSize = 100;

        private readonly IMediaFileService _mediaFileService;
        private readonly IImportlFileService _importFileService;
        private readonly IMovieService _movieService;
        private readonly IVideoHashService _videoHashService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Rows that fail to hash stay NULL; the per-table Id watermark moves past them so they
        // don't block later rows, and wraps to 0 at the end of a pass so they are retried once per pass
        private int _movieFileWatermark;
        private int _importFileWatermark;

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
            var movieFiles = _mediaFileService.GetFilesWithoutOsHash(_movieFileWatermark, MaxFilesPerRun);

            // Fewer rows than asked means this pass reached the end of the table: start over next run
            _movieFileWatermark = movieFiles.Count < MaxFilesPerRun ? 0 : movieFiles[^1].Id;

            var remaining = MaxFilesPerRun - movieFiles.Count;
            var importFiles = new List<ImportFile>();

            if (remaining > 0)
            {
                importFiles = _importFileService.GetFilesWithoutOsHash(_importFileWatermark, remaining);
                _importFileWatermark = importFiles.Count < remaining ? 0 : importFiles[^1].Id;
            }

            var total = movieFiles.Count + importFiles.Count;

            if (total == 0)
            {
                return;
            }

            var movies = LoadMovies(movieFiles);
            var processed = 0;
            var hashed = 0;
            var pendingMovieFiles = new List<MovieFile>();
            var pendingImportFiles = new List<ImportFile>();

            // Batch writes go straight to the repositories: no per-file events for the UI
            foreach (var movieFile in movieFiles)
            {
                _logger.ProgressInfo("Hashing file {0}/{1}", ++processed, total);

                if (TryHash(ResolvePath(movieFile, movies), out var osHash, out var modifiedAt))
                {
                    movieFile.OsHash = osHash;
                    movieFile.HashedFileModifiedAt = modifiedAt;
                    pendingMovieFiles.Add(movieFile);
                    hashed++;
                }

                pendingMovieFiles = Flush(pendingMovieFiles, _mediaFileService.UpdateMany, FlushSize);
            }

            foreach (var importFile in importFiles)
            {
                _logger.ProgressInfo("Hashing file {0}/{1}", ++processed, total);

                if (TryHash(importFile.Path, out var osHash, out var modifiedAt))
                {
                    importFile.OsHash = osHash;
                    importFile.HashedFileModifiedAt = modifiedAt;
                    pendingImportFiles.Add(importFile);
                    hashed++;
                }

                pendingImportFiles = Flush(pendingImportFiles, _importFileService.UpdateMany, FlushSize);
            }

            Flush(pendingMovieFiles, _mediaFileService.UpdateMany, 1);
            Flush(pendingImportFiles, _importFileService.UpdateMany, 1);

            _logger.ProgressInfo("Hashed {0} of {1} files", hashed, total);
        }

        // Writes the pending rows once there are at least `threshold` of them; returns the list to keep filling
        private static List<T> Flush<T>(List<T> pending, Action<List<T>> save, int threshold)
        {
            if (pending.Count < threshold)
            {
                return pending;
            }

            save(pending);

            return new List<T>();
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

            // A hash without a last-write stamp can't be compared on rescan, so treat it as a failure
            return osHash != null && modifiedAt != null;
        }
    }
}
