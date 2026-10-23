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

        // Failures before a run's first success that stop the run, unstamped
        internal const int HeldFailureLimit = 5;

        // A phash run stops starting new files after this much wall time; the next run continues
        internal static readonly TimeSpan PHashRunBudget = TimeSpan.FromMinutes(15);

        private readonly IConfigService _configService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IImportlFileService _importFileService;
        private readonly IMovieService _movieService;
        private readonly IVideoHashService _videoHashService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Missing files are skipped without a phash stamp, so these cursors keep them from coming back within a pass
        private readonly PHashCursor _movieFilePHashCursor = new();
        private readonly PHashCursor _importFilePHashCursor = new();

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
                _logger.ProgressDebug("Hashing file {0}/{1}", ++processed, total);

                if (TryHash(ResolvePath(movieFile, movies), out var osHash, out var modifiedAt))
                {
                    movieFile.OsHash = osHash;
                    movieFile.HashedFileModifiedAt = modifiedAt;
                    pendingMovieFiles.Add(movieFile);
                    hashed++;
                }

                pendingMovieFiles = Flush(pendingMovieFiles, _mediaFileService.SaveOsHashes, FlushSize);
            }

            // Persist the movie-file remainder before the import loop so a throw there can't lose it
            Flush(pendingMovieFiles, _mediaFileService.SaveOsHashes, 1);

            foreach (var importFile in importFiles)
            {
                _logger.ProgressDebug("Hashing file {0}/{1}", ++processed, total);

                if (TryHash(importFile.Path, out var osHash, out var modifiedAt))
                {
                    importFile.OsHash = osHash;
                    importFile.HashedFileModifiedAt = modifiedAt;
                    pendingImportFiles.Add(importFile);
                    hashed++;
                }

                pendingImportFiles = Flush(pendingImportFiles, _importFileService.SaveOsHashes, FlushSize);
            }

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
                _movieFilePHashCursor,
                _mediaFileService.GetFilesNeedingPHash,
                f => f.Id,
                chunk =>
                {
                    var movies = LoadMovies(chunk);

                    return f => ResolvePath(f, movies);
                },
                (f, hash) => f.PHash = hash,
                (f, at) =>
                {
                    f.PHashAttemptedAt = at;
                    _mediaFileService.SavePHashes(new List<MovieFile> { f });
                },
                run);

            finished = finished && HashChunks(
                _importFilePHashCursor,
                _importFileService.GetFilesNeedingPHash,
                f => f.Id,
                _ => f => f.Path,
                (f, hash) => f.PHash = hash,
                (f, at) =>
                {
                    f.PHashAttemptedAt = at;
                    _importFileService.SavePHashes(new List<ImportFile> { f });
                },
                run);

            if (run.TurnedOff)
            {
                _logger.ProgressInfo("Perceptual hashing turned off, stopping after {0} files", run.Processed);
            }
            else if (run.Stopped)
            {
                // The warning was logged when the breaker tripped
            }
            else if (run.Processed > 0)
            {
                _logger.ProgressInfo("Computed perceptual hashes for {0} of {1} files{2}", run.Hashed, run.Processed, finished ? string.Empty : " (time budget reached)");
            }
        }

        // Hashes chunk after chunk until the pass reaches the end of the table (true), the deadline
        // passes, the setting is turned off or the circuit breaker trips (false).
        // An existing file that was tried gets an attempted stamp and is saved on its own, so it leaves
        // the "needing phash" query and a long run keeps its work across a shutdown. Missing files are
        // skipped unstamped; the cursor moves past them so they don't come back within the pass.
        // Failures before the run's first success are held unstamped: if the first few all fail the
        // cause is probably system-wide (ffmpeg), and stamping would burn the library.
        private bool HashChunks<T>(PHashCursor cursor,
                                   Func<int, int, List<T>> fetch,
                                   Func<T, int> idOf,
                                   Func<List<T>, Func<T, string>> pathResolverFor,
                                   Action<T, string> setHash,
                                   Action<T, DateTime> stamp,
                                   PHashRun run)
        {
            while (true)
            {
                var chunk = fetch(cursor.AfterId, PHashChunkSize);

                // Fewer rows than asked means this chunk reaches the end of the table
                var lastChunk = chunk.Count < PHashChunkSize;
                var resolvePath = pathResolverFor(chunk);

                foreach (var file in chunk)
                {
                    if (ShouldStop(run))
                    {
                        return false;
                    }

                    run.Processed++;

                    var path = resolvePath(file);

                    if (path == null || !_diskProvider.FileExists(path))
                    {
                        _logger.Debug("Skipping phash for missing file {0}", path);
                        cursor.AfterId = idOf(file);

                        continue;
                    }

                    _logger.ProgressDebug("Computing perceptual hash {0}", run.Processed);

                    var pHash = _videoHashService.ComputePHash(path);

                    if (!RecordPHashResult(file, pHash, setHash, () => stamp(file, UtcNow()), run))
                    {
                        // The held rows stay unstamped, so the next run starts the pass again
                        cursor.AfterId = 0;

                        return false;
                    }

                    cursor.AfterId = idOf(file);
                }

                if (lastChunk)
                {
                    cursor.AfterId = 0;

                    return true;
                }
            }
        }

        private bool ShouldStop(PHashRun run)
        {
            // Read live each time: turning the setting off is how a user stops a running task
            if (!_configService.EnablePerceptualHashing)
            {
                run.TurnedOff = true;

                return true;
            }

            return UtcNow() >= run.Deadline;
        }

        // Returns false when the circuit breaker tripped and the run must stop
        private bool RecordPHashResult<T>(T file, string pHash, Action<T, string> setHash, Action stamp, PHashRun run)
        {
            if (pHash != null)
            {
                setHash(file, pHash);
                run.Hashed++;
                stamp();

                // The first success shows the failures weren't system-wide: stamp the ones held back
                run.AnySuccess = true;
                run.ReleaseHeld();

                return true;
            }

            if (run.AnySuccess)
            {
                stamp();

                return true;
            }

            run.Held.Add(stamp);

            if (run.Held.Count < HeldFailureLimit)
            {
                return true;
            }

            _logger.Warn("Perceptual hashing failed for the first {0} files of this run; stopping without marking them. Check ffmpeg/ffprobe.", HeldFailureLimit);
            run.Stopped = true;

            return false;
        }

        private sealed class PHashCursor
        {
            public int AfterId { get; set; }
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
            public bool TurnedOff { get; set; }
            public bool Stopped { get; set; }
            public bool AnySuccess { get; set; }
            public List<Action> Held { get; } = new();

            public void ReleaseHeld()
            {
                foreach (var stamp in Held)
                {
                    stamp();
                }

                Held.Clear();
            }
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
