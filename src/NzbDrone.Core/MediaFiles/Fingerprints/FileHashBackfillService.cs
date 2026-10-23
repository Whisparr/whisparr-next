using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Fingerprints.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    public class FileHashBackfillService : IExecute<GenerateOsHashesCommand>, IExecute<GenerateVideoHashesCommand>
    {
        // Files fetched per query during a phash run
        internal const int PHashChunkSize = 50;

        // Upper bound on files hashed by a single run; the next scheduled run picks up the rest
        internal const int MaxFilesPerRun = 2000;

        // Results are persisted in batches this size, so a restart mid-run keeps progress
        internal const int FlushSize = 100;

        // A phash run stops starting new files after this much wall time; the next run continues
        internal static readonly TimeSpan PHashRunBudget = TimeSpan.FromMinutes(15);

        private readonly IConfigService _configService;
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

        public FileHashBackfillService(IConfigService configService,
                                       IMediaFileService mediaFileService,
                                       IImportlFileService importFileService,
                                       IMovieService movieService,
                                       IVideoHashService videoHashService,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            UtcNow = () => DateTime.UtcNow;
            _configService = configService;
            _mediaFileService = mediaFileService;
            _importFileService = importFileService;
            _movieService = movieService;
            _videoHashService = videoHashService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        // Replaceable so tests can drive the run budget
        internal Func<DateTime> UtcNow { get; set; }

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

                pendingMovieFiles = Flush(pendingMovieFiles, _mediaFileService.SaveOsHashes, FlushSize);
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

                pendingImportFiles = Flush(pendingImportFiles, _importFileService.SaveOsHashes, FlushSize);
            }

            Flush(pendingMovieFiles, _mediaFileService.SaveOsHashes, 1);
            Flush(pendingImportFiles, _importFileService.SaveOsHashes, 1);

            _logger.ProgressInfo("Hashed {0} of {1} files", hashed, total);
        }

        public void Execute(GenerateVideoHashesCommand message)
        {
            if (!_configService.EnablePerceptualHashing || !_videoHashService.CanComputePHash)
            {
                return;
            }

            var run = new PHashRun(UtcNow() + PHashRunBudget);

            // Movie files first, then import files, all inside one time budget
            var finished = HashChunks(
                () => _mediaFileService.GetFilesNeedingPHash(PHashChunkSize),
                chunk =>
                {
                    var movies = LoadMovies(chunk);

                    return f => ResolvePath(f, movies);
                },
                (f, hash) => f.PHash = hash,
                (f, at) => f.PHashAttemptedAt = at,
                f => _mediaFileService.SavePHashes(new List<MovieFile> { f }),
                run);

            finished = finished && HashChunks(
                () => _importFileService.GetFilesNeedingPHash(PHashChunkSize),
                _ => f => f.Path,
                (f, hash) => f.PHash = hash,
                (f, at) => f.PHashAttemptedAt = at,
                f => _importFileService.SavePHashes(new List<ImportFile> { f }),
                run);

            if (run.Processed > 0)
            {
                _logger.ProgressInfo("Computed perceptual hashes for {0} of {1} files{2}", run.Hashed, run.Processed, finished ? string.Empty : " (time budget reached)");
            }
        }

        // Hashes chunk after chunk until nothing is left (true) or the deadline passes (false).
        // Every processed row gets an attempted stamp and is saved on its own, so it leaves the
        // "needing phash" query and a long run keeps its work across a shutdown.
        private bool HashChunks<T>(Func<List<T>> fetch,
                                   Func<List<T>, Func<T, string>> pathResolverFor,
                                   Action<T, string> setHash,
                                   Action<T, DateTime> setAttemptedAt,
                                   Action<T> save,
                                   PHashRun run)
        {
            while (true)
            {
                var chunk = fetch();

                if (chunk.Count == 0)
                {
                    return true;
                }

                var resolvePath = pathResolverFor(chunk);

                foreach (var file in chunk)
                {
                    if (UtcNow() >= run.Deadline)
                    {
                        return false;
                    }

                    run.Processed++;

                    if (TryComputePHash(resolvePath(file), out var pHash))
                    {
                        setHash(file, pHash);
                        run.Hashed++;
                    }

                    setAttemptedAt(file, UtcNow());
                    save(file);
                }
            }
        }

        // A missing file or a failed hash returns false; the caller still marks the row as attempted
        private bool TryComputePHash(string path, out string pHash)
        {
            pHash = null;

            if (path == null || !_diskProvider.FileExists(path))
            {
                _logger.Debug("Skipping phash for missing file {0}", path);

                return false;
            }

            pHash = _videoHashService.ComputePHash(path);

            return pHash != null;
        }

        private sealed class PHashRun
        {
            public PHashRun(DateTime deadline)
            {
                Deadline = deadline;
            }

            public DateTime Deadline { get; }
            public int Processed { get; set; }
            public int Hashed { get; set; }
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
