using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.LegacyMigration
{
    // Re-identifies the Eros movies that had no twin in the chosen metadata source and links their parked files
    public class ErosSeedImportRunner : ILegacyImportRunner
    {
        private const string Area = "Movies";

        private readonly ILegacyMovieSeedRepository _seedRepository;
        private readonly ISearchForNewMovie _searchForNewMovie;
        private readonly IAddMovieService _addMovieService;
        private readonly IMovieService _movieService;
        private readonly IImportlFileService _importFileService;
        private readonly ILegacyFileLinker _fileLinker;
        private readonly ILegacyMigrationService _migrationService;
        private readonly IDiskProvider _diskProvider;
        private readonly ISkyhookPacer _pacer;
        private readonly Logger _logger;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public ErosSeedImportRunner(ILegacyMovieSeedRepository seedRepository,
                                    ISearchForNewMovie searchForNewMovie,
                                    IAddMovieService addMovieService,
                                    IMovieService movieService,
                                    IImportlFileService importFileService,
                                    ILegacyFileLinker fileLinker,
                                    ILegacyMigrationService migrationService,
                                    IDiskProvider diskProvider,
                                    ISkyhookPacer pacer,
                                    Logger logger)
        {
            _pacer = pacer;
            _seedRepository = seedRepository;
            _searchForNewMovie = searchForNewMovie;
            _addMovieService = addMovieService;
            _movieService = movieService;
            _importFileService = importFileService;
            _fileLinker = fileLinker;
            _migrationService = migrationService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public bool Handles(LegacyMigrationState state)
        {
            return state.SourceKind == LegacyKind.Eros;
        }

        public LegacyImportPass RunPass(LegacyMigrationState state, ILegacyImportProgress progress, bool recordOutages)
        {
            var seeds = _seedRepository.All().ToList();

            // Seeds resolved on an earlier pass count as done so the bar stays consistent across passes
            progress.Total(seeds.Count);

            var alreadyResolved = seeds.Count(s => s.Resolved);

            if (alreadyResolved > 0)
            {
                progress.Advance(Area, alreadyResolved);
            }

            var pass = new LegacyImportPass();
            var failures = new List<(LegacyMovieSeed Seed, Exception Error)>();
            var succeeded = 0;
            var now = _pacer.UtcNow;

            foreach (var seed in seeds.Where(s => !s.Resolved))
            {
                // A seed backing off from an earlier failure waits for its own turn
                if (!LegacyRetryPolicy.IsDue(seed.Attempts, seed.LastAttemptAt, now))
                {
                    pass.Pending++;
                    pass.WaitingRetry++;
                    pass.NextDue = LegacyRetryPolicy.Earlier(pass.NextDue, LegacyRetryPolicy.DueAt(seed.Attempts, seed.LastAttemptAt));
                    continue;
                }

                pass.Attempted++;

                try
                {
                    Resolve(state, seed);
                    progress.Advance(Area);
                    succeeded++;
                }
                catch (Exception ex) when (ex is HttpException || ex is WebException || ex is TimeoutException)
                {
                    _logger.Warn(ex, "Could not re-identify {0} ({1}), will retry", seed.Title, seed.Year);

                    failures.Add((seed, ex));
                }
                catch (Exception ex)
                {
                    // Retrying won't help; report it and move on so one bad seed can't hold the migration open
                    _logger.Warn(ex, "Could not import {0} ({1})", seed.Title, seed.Year);

                    _migrationService.Report(LegacyReportCategory.NeedsAttention, Area, $"{seed.Title} ({seed.Year})", ex.Message);
                    MarkResolved(seed);
                    progress.Advance(Area);
                }
            }

            // Everything worked on failed transiently, so the source is down rather than the seeds: don't spend their attempts
            pass.Outage = failures.Count > 0 && succeeded == 0;

            foreach (var (seed, error) in failures)
            {
                if (pass.Outage && !recordOutages)
                {
                    pass.Pending++;
                    pass.WaitingRetry++;
                }
                else if (RecordFailure(seed, error))
                {
                    progress.Advance(Area);
                }
                else
                {
                    pass.Pending++;
                    pass.WaitingRetry++;
                    pass.NextDue = LegacyRetryPolicy.Earlier(pass.NextDue, LegacyRetryPolicy.DueAt(seed.Attempts, seed.LastAttemptAt));
                }
            }

            return pass;
        }

        private void Resolve(LegacyMigrationState state, LegacyMovieSeed seed)
        {
            var candidate = Find(state, seed);

            if (candidate == null)
            {
                ReportUnmatched(seed);
                MarkResolved(seed);

                return;
            }

            var importFile = seed.ImportFileId.HasValue ? _importFileService.Get(seed.ImportFileId.Value) : null;
            var movie = _movieService.FindByForeignId(candidate.ForeignId);

            if (movie == null)
            {
                candidate.Monitored = seed.Monitored;
                candidate.QualityProfileId = seed.QualityProfileId;
                candidate.RootFolderPath = seed.RootFolderPath;
                candidate.Tags = ParseTags(seed.Tags);
                candidate.AddOptions = new AddMovieOptions { SearchForMovie = false };

                // The stub's folder is the file's own, so a refresh of a movie that doesn't exist on disk yet
                // can never see the file as missing and clean its row away
                var fileFolder = importFile == null ? null : Path.GetDirectoryName(importFile.Path);

                if (fileFolder.IsNotNullOrWhiteSpace() && !fileFolder.PathEquals(seed.RootFolderPath))
                {
                    candidate.Path = fileFolder;
                }

                // Inline, so no refresh queued by the add is still running with a stale movie when the file is linked.
                // Errors are not ignored: a failed add is the seed's failure, not an empty result.
                movie = _addMovieService.AddMovies(new List<Movie> { candidate }, false, refreshInline: true).FirstOrDefault()
                    ?? _movieService.FindByForeignId(candidate.ForeignId)
                    ?? throw new InvalidOperationException($"{seed.Title} ({seed.Year}) was not added");
            }

            if (seed.ImportFileId.HasValue)
            {
                LinkImportFile(seed, movie, importFile);
            }

            MarkResolved(seed);
        }

        private Movie Find(LegacyMigrationState state, LegacyMovieSeed seed)
        {
            if (state.MovieSource == MovieMetadataType.TMDB && seed.TmdbId > 0)
            {
                var byId = _searchForNewMovie.SearchForNewMovie($"tmdb:{seed.TmdbId}")
                    .Where(m => m.TmdbId == seed.TmdbId)
                    .ToList();

                return byId.Count == 1 ? byId[0] : null;
            }

            var cleanTitle = seed.Title.CleanMovieTitle();
            var byTitle = _searchForNewMovie.SearchForNewMovie($"{seed.Title} {seed.Year}")
                .Where(m => m.Title.CleanMovieTitle() == cleanTitle && m.Year == seed.Year)
                .ToList();

            return byTitle.Count == 1 ? byTitle[0] : null;
        }

        private void LinkImportFile(LegacyMovieSeed seed, Movie movie, ImportFile importFile)
        {
            if (importFile == null)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, Area, $"{seed.Title} ({seed.Year})", "Parked file no longer exists");

                return;
            }

            var outcome = _fileLinker.Link(movie, importFile.Path, file =>
            {
                file.Size = _diskProvider.GetFileSize(importFile.Path);
                file.DateAdded = DateTime.UtcNow;
            });

            if (outcome == LinkOutcome.Linked || outcome == LinkOutcome.AlreadyLinked)
            {
                _importFileService.Remove(importFile.Id);

                return;
            }

            // The file stays parked in the import list either way
            var detail = outcome == LinkOutcome.SceneHasOtherFile
                ? $"Scene already has a file; legacy also had {importFile.Path}"
                : $"File could not be linked: {outcome}";

            _migrationService.Report(LegacyReportCategory.NeedsAttention, Area, importFile.Path, detail);
        }

        private void ReportUnmatched(LegacyMovieSeed seed)
        {
            _migrationService.Report(LegacyReportCategory.NeedsAttention, Area, $"{seed.Title} ({seed.Year})", seed.LegacyForeignId);
        }

        private void MarkResolved(LegacyMovieSeed seed)
        {
            seed.Resolved = true;
            _seedRepository.Update(seed);
        }

        // Returns true when the seed was given up on
        private bool RecordFailure(LegacyMovieSeed seed, Exception ex)
        {
            seed.Attempts++;
            seed.LastAttemptAt = _pacer.UtcNow;

            var giveUp = LegacyRetryPolicy.ShouldGiveUp(seed.Attempts);

            if (giveUp)
            {
                seed.Resolved = true;
                _migrationService.Report(LegacyReportCategory.NeedsAttention, Area, $"{seed.Title} ({seed.Year})", $"{seed.LegacyForeignId}: {ex.Message}");
            }

            _seedRepository.Update(seed);

            return giveUp;
        }

        private static HashSet<int> ParseTags(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new HashSet<int>();
            }

            return JsonSerializer.Deserialize<HashSet<int>>(json) ?? new HashSet<int>();
        }
    }
}
