using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Net;
using NLog;
using Npgsql;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.LegacyMigration
{
    // The pass loop shared by the runners that turn the staged v2 series into v5 studios: retries, outages,
    // parking, and the done-marking. A subclass supplies the scene source in Import
    public abstract class V2SeriesImportRunnerBase : ILegacyImportRunner
    {
        protected const string StudioArea = "Studios";
        protected const string FileArea = "Files";

        // The same converters the repositories use to read these columns
        private static readonly EmbeddedDocumentConverter<QualityModel> QualityConverter = new(new QualityIntConverter());
        private static readonly EmbeddedDocumentConverter<List<Language>> LanguagesConverter = new(new LanguageIntConverter());
        private static readonly EmbeddedDocumentConverter<HashSet<int>> TagsConverter = new();

        protected readonly ILegacyV2SeriesRepository _seriesRepository;
        protected readonly ILegacyV2EpisodeRepository _episodeRepository;
        protected readonly ILegacyV2EpisodeFileRepository _fileRepository;
        protected readonly ITpdbIdResolver _idResolver;
        protected readonly IStudioService _studioService;
        protected readonly ILegacyFileLinker _fileLinker;
        protected readonly ILegacyMigrationService _migrationService;
        protected readonly ISkyhookPacer _pacer;
        protected readonly Logger _logger;

        // The studio id of each series whose site resolved this run, to find the studio again if the series is given up on
        protected readonly Dictionary<int, string> CreatedStudioIds = new();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        protected V2SeriesImportRunnerBase(ILegacyV2SeriesRepository seriesRepository,
                                           ILegacyV2EpisodeRepository episodeRepository,
                                           ILegacyV2EpisodeFileRepository fileRepository,
                                           ITpdbIdResolver idResolver,
                                           IStudioService studioService,
                                           ILegacyFileLinker fileLinker,
                                           ILegacyMigrationService migrationService,
                                           ISkyhookPacer pacer,
                                           Logger logger)
        {
            _pacer = pacer;
            _seriesRepository = seriesRepository;
            _episodeRepository = episodeRepository;
            _fileRepository = fileRepository;
            _idResolver = idResolver;
            _studioService = studioService;
            _fileLinker = fileLinker;
            _migrationService = migrationService;
            _logger = logger;
        }

        public abstract bool Handles(LegacyMigrationState state);

        protected abstract void Import(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, ILegacyImportProgress progress);

        public LegacyImportPass RunPass(LegacyMigrationState state, ILegacyImportProgress progress, bool recordOutages)
        {
            var allSeries = _seriesRepository.All().ToList();
            var episodes = _episodeRepository.All().ToList();
            var files = _fileRepository.All().ToList();

            var pendingSeries = allSeries.Where(s => !s.Done).OrderBy(s => s.LegacyId).ToList();
            var pendingFiles = files.Where(f => !f.Done).ToList();

            progress.Total(pendingSeries.Count + pendingFiles.Count);

            var pass = new LegacyImportPass();
            var failures = new List<SeriesFailure>();
            var succeeded = 0;
            var now = _pacer.UtcNow;

            foreach (var series in pendingSeries)
            {
                // A series backing off from an earlier failure waits for its own turn
                if (!LegacyRetryPolicy.IsDue(series.Attempts, series.LastAttemptAt, now))
                {
                    pass.WaitingRetry++;
                    pass.NextDue = LegacyRetryPolicy.Earlier(pass.NextDue, LegacyRetryPolicy.DueAt(series.Attempts, series.LastAttemptAt));
                    continue;
                }

                var seriesEpisodes = episodes.Where(e => e.LegacySeriesId == series.LegacyId).OrderBy(e => e.LegacyId).ToList();
                var seriesFiles = pendingFiles.Where(f => f.LegacySeriesId == series.LegacyId).OrderBy(f => f.LegacyId).ToList();

                pass.Attempted++;

                switch (TryImport(series, seriesEpisodes, seriesFiles, progress, out var error))
                {
                    case ImportResult.Imported:
                        succeeded++;
                        break;
                    case ImportResult.Waiting:
                        pass.Waiting++;
                        break;
                    case ImportResult.Transient:
                        failures.Add(new SeriesFailure(series, seriesEpisodes, seriesFiles, error));
                        break;
                }
            }

            // Everything worked on failed transiently, so the source is down rather than the series: don't spend their attempts
            pass.Outage = failures.Count > 0 && succeeded == 0 && pass.Waiting == 0;

            if (pass.Outage && !recordOutages)
            {
                _logger.Warn("Every one of the {0} studios tried failed transiently, so no attempts were spent on them", failures.Count);

                pass.WaitingRetry += failures.Count;
            }
            else
            {
                foreach (var failure in failures)
                {
                    RecordTransientFailure(failure, progress, pass);
                }
            }

            ParkOrphanFiles(pendingFiles, allSeries, progress);

            pass.Pending = pendingSeries.Count(s => !s.Done) + pendingFiles.Count(f => !f.Done);

            return pass;
        }

        private ImportResult TryImport(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, ILegacyImportProgress progress, out Exception error)
        {
            error = null;

            try
            {
                Import(series, episodes, files, progress);

                return ImportResult.Imported;
            }
            catch (ResolvePendingException ex)
            {
                // Skyhook is still working on it: not a failure, so no attempt is spent, but the wait has a ceiling
                _logger.Debug(ex, "Still waiting on TPDB for {0}", series.Title);

                if (!WaitedTooLong(series))
                {
                    return ImportResult.Waiting;
                }

                const string reason = "TPDB did not finish resolving ids within 24 hours";

                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, reason);
                ParkAndFinish(series, episodes, files, reason, progress);

                return ImportResult.Reported;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _logger.Warn(ex, "Could not import {0}, will retry", series.Title);

                error = ex;

                return ImportResult.Transient;
            }
            catch (Exception ex)
            {
                // Retrying won't help; report it and move on so one bad series can't hold the migration open
                _logger.Warn(ex, "Could not import {0}", series.Title);

                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, ex.Message);
                ParkAndFinish(series, episodes, files, $"Import failed: {ex.Message}", progress);

                return ImportResult.Reported;
            }
        }

        private void RecordTransientFailure(SeriesFailure failure, ILegacyImportProgress progress, LegacyImportPass pass)
        {
            var series = failure.Series;

            if (RecordFailure(series, failure.Error))
            {
                ParkAndFinish(series, failure.Episodes, failure.Files, $"Import failed: {failure.Error.Message}", progress);

                return;
            }

            pass.WaitingRetry++;
            pass.NextDue = LegacyRetryPolicy.Earlier(pass.NextDue, LegacyRetryPolicy.DueAt(series.Attempts, series.LastAttemptAt));
        }

        protected void ApplyV2Settings(LegacyV2Series series, Studio studio)
        {
            studio.Monitored = series.Monitored;
            studio.WhisparrMonitorNewItems = series.MonitorNewItems == 0;
            studio.SearchOnAdd = false;
            _studioService.Update(studio);
        }

        // A series given up on keeps the studio it already created, so the studio still gets what the user had set in v2
        private void ApplyV2SettingsToCreatedStudio(LegacyV2Series series)
        {
            if (!CreatedStudioIds.Remove(series.LegacyId, out var siteUuid))
            {
                return;
            }

            try
            {
                var studio = _studioService.FindByForeignId(siteUuid);

                if (studio != null)
                {
                    ApplyV2Settings(series, studio);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not apply the v2 settings to the studio of {0}", series.Title);

                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, $"v2 studio settings not applied: {ex.Message}");
            }
        }

        // Null when TPDB doesn't know the site
        protected string ResolveSite(LegacyV2Series series)
        {
            if (series.TpdbSiteId == 0)
            {
                return null;
            }

            var result = _idResolver.Resolve(TpdbIdKind.Site, new[] { series.TpdbSiteId });

            if (result.Throttled)
            {
                throw new ResolveThrottledException(series.Title);
            }

            if (result.Pending)
            {
                throw new ResolvePendingException(series.Title);
            }

            return result.Ids.TryGetValue(series.TpdbSiteId, out var uuid) ? uuid : null;
        }

        // Also reachable from runners that copy file metadata without subclassing
        internal static void Fill(MovieFile movieFile, LegacyV2EpisodeFile file)
        {
            movieFile.Size = file.Size;
            movieFile.Quality = ParseQuality(file.Quality);
            movieFile.Languages = ParseLanguages(file.Languages);
            movieFile.ReleaseGroup = file.ReleaseGroup;
            movieFile.SceneName = file.SceneName;
            movieFile.OriginalFilePath = file.OriginalFilePath;
            movieFile.IndexerFlags = (IndexerFlags)file.IndexerFlags;
            movieFile.DateAdded = file.DateAdded;
        }

        internal static QualityModel ParseQuality(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? new QualityModel() : QualityConverter.Parse(json);
        }

        internal static List<Language> ParseLanguages(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? new List<Language>() : LanguagesConverter.Parse(json);
        }

        internal static HashSet<int> ParseTags(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? new HashSet<int>() : TagsConverter.Parse(json) ?? new HashSet<int>();
        }

        // Parks the files of a series that can't be imported, then marks the series and its episodes done
        protected void ParkAndFinish(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, string reason, ILegacyImportProgress progress)
        {
            ApplyV2SettingsToCreatedStudio(series);

            foreach (var file in files.Where(f => !f.Done))
            {
                try
                {
                    _fileLinker.Park(file.Path, series.QualityProfileId, FileArea, reason);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not park {0}", file.Path);

                    _migrationService.Report(LegacyReportCategory.NeedsAttention, FileArea, file.Path, ex.Message);
                }

                MarkFileDone(file, progress);
            }

            MarkSeriesDone(series, episodes, progress);
        }

        private void ParkOrphanFiles(List<LegacyV2EpisodeFile> pendingFiles, List<LegacyV2Series> allSeries, ILegacyImportProgress progress)
        {
            var knownSeries = allSeries.ToDictionary(s => s.LegacyId);

            // An orphan has no profile of its own, so it borrows the first series' one
            var fallbackProfileId = allSeries.OrderBy(s => s.LegacyId).FirstOrDefault()?.QualityProfileId ?? 0;

            // A file still not done here has no series left to process it: its series is missing, or finished without it
            foreach (var file in pendingFiles.Where(f => !f.Done && (!knownSeries.TryGetValue(f.LegacySeriesId, out var owner) || owner.Done)))
            {
                var hasSeries = knownSeries.TryGetValue(file.LegacySeriesId, out var series);

                try
                {
                    _fileLinker.Park(
                        file.Path,
                        hasSeries ? series.QualityProfileId : fallbackProfileId,
                        FileArea,
                        hasSeries ? "Series finished without this file" : "No series in v2");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not park {0}", file.Path);

                    _migrationService.Report(LegacyReportCategory.NeedsAttention, FileArea, file.Path, ex.Message);
                }

                MarkFileDone(file, progress);
            }
        }

        protected void MarkFileDone(LegacyV2EpisodeFile file, ILegacyImportProgress progress)
        {
            file.Done = true;
            _fileRepository.Update(file);
            progress.Advance(FileArea);
        }

        protected void MarkSeriesDone(LegacyV2Series series, List<LegacyV2Episode> episodes, ILegacyImportProgress progress)
        {
            foreach (var episode in episodes.Where(e => !e.Done))
            {
                episode.Done = true;
            }

            _episodeRepository.UpdateMany(episodes);

            series.Done = true;
            _seriesRepository.Update(series);
            progress.Advance(StudioArea);
        }

        private enum ImportResult
        {
            Imported,
            Waiting,
            Transient,
            Reported
        }

        private sealed record SeriesFailure(LegacyV2Series Series, List<LegacyV2Episode> Episodes, List<LegacyV2EpisodeFile> Files, Exception Error);

        // Remembers when the series first waited on a 202; true once that is longer ago than the ceiling allows
        private bool WaitedTooLong(LegacyV2Series series)
        {
            var now = _pacer.UtcNow;

            if (series.WaitingSince == null)
            {
                series.WaitingSince = now;
                _seriesRepository.Update(series);
            }

            return now - series.WaitingSince.Value > LegacyRetryPolicy.MaxPendingWait;
        }

        // Returns true when the series was given up on
        private bool RecordFailure(LegacyV2Series series, Exception ex)
        {
            series.Attempts++;
            series.LastAttemptAt = _pacer.UtcNow;

            var giveUp = LegacyRetryPolicy.ShouldGiveUp(series.Attempts);

            if (giveUp)
            {
                var detail = ex is CatalogIncompleteException
                    ? $"{ex.Message}; a later refresh may add the missing scenes using the studio's monitoring settings, so unmonitor the studio first if you do not want them grabbed"
                    : ex.Message;

                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, detail);
            }
            else
            {
                _seriesRepository.Update(series);
            }

            return giveUp;
        }

        protected static bool IsTransient(Exception ex)
        {
            return ex is HttpException or WebException or TimeoutException or ResolveThrottledException or FingerprintTryLaterException or CatalogIncompleteException
                || ex is SQLiteException { ResultCode: SQLiteErrorCode.Busy or SQLiteErrorCode.Locked }
                || ex is NpgsqlException { IsTransient: true };
        }

        public sealed class ResolvePendingException : Exception
        {
            public ResolvePendingException(string subject)
                : base($"TPDB is still resolving ids for {subject}")
            {
            }
        }

        // Skyhook answered 429 or 503: a transient failure, unlike the 202 above
        public sealed class ResolveThrottledException : Exception
        {
            public ResolveThrottledException(string subject)
                : base($"TPDB is throttling id lookups for {subject}")
            {
            }
        }
    }

    // A fingerprint lookup that should be tried again on a later pass: transient, so no attempt is wasted on a verdict
    public sealed class FingerprintTryLaterException : Exception
    {
        public FingerprintTryLaterException(string message)
            : base(message)
        {
        }
    }
}
