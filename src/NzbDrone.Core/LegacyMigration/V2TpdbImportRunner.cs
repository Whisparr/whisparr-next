using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.LegacyMigration
{
    // Turns the staged v2 library into v5 studios with their full TPDB catalog, copies the monitored state,
    // and links each v2 file to its scene in place
    public class V2TpdbImportRunner : ILegacyImportRunner
    {
        private const string StudioArea = "Studios";
        private const string FileArea = "Files";

        // The same converters the repositories use to read these columns
        private static readonly EmbeddedDocumentConverter<QualityModel> QualityConverter = new(new QualityIntConverter());
        private static readonly EmbeddedDocumentConverter<List<Language>> LanguagesConverter = new(new LanguageIntConverter());
        private static readonly EmbeddedDocumentConverter<HashSet<int>> TagsConverter = new();

        private readonly ILegacyV2SeriesRepository _seriesRepository;
        private readonly ILegacyV2EpisodeRepository _episodeRepository;
        private readonly ILegacyV2EpisodeFileRepository _fileRepository;
        private readonly ITpdbIdResolver _idResolver;
        private readonly IAddStudioService _addStudioService;
        private readonly IStudioService _studioService;
        private readonly ISceneCatalogImporter _catalogImporter;
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IAddMovieService _addMovieService;
        private readonly IMovieService _movieService;
        private readonly ILegacyFileLinker _fileLinker;
        private readonly ILegacyMigrationService _migrationService;
        private readonly ISkyhookPacer _pacer;
        private readonly Logger _logger;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public V2TpdbImportRunner(ILegacyV2SeriesRepository seriesRepository,
                                  ILegacyV2EpisodeRepository episodeRepository,
                                  ILegacyV2EpisodeFileRepository fileRepository,
                                  ITpdbIdResolver idResolver,
                                  IAddStudioService addStudioService,
                                  IStudioService studioService,
                                  ISceneCatalogImporter catalogImporter,
                                  IProvideMovieInfo movieInfo,
                                  IAddMovieService addMovieService,
                                  IMovieService movieService,
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
            _addStudioService = addStudioService;
            _studioService = studioService;
            _catalogImporter = catalogImporter;
            _movieInfo = movieInfo;
            _addMovieService = addMovieService;
            _movieService = movieService;
            _fileLinker = fileLinker;
            _migrationService = migrationService;
            _logger = logger;
        }

        public bool Handles(LegacyMigrationState state)
        {
            return state.SourceKind == LegacyKind.V2 && state.SceneSource == SceneMetadataType.Tpdb;
        }

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
                _logger.Debug("{0} for {1}", ex.Message, series.Title);

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

        private void Import(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, ILegacyImportProgress progress)
        {
            var siteUuid = ResolveSite(series);

            if (siteUuid == null)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, "Studio not found on TPDB");
                ParkAndFinish(series, episodes, files, "Studio not found on TPDB", progress);

                return;
            }

            var sceneUuids = ResolveScenes(episodes);
            var episodeByUuid = new Dictionary<string, LegacyV2Episode>();

            foreach (var episode in episodes.Where(e => sceneUuids.ContainsKey(e.TpdbSceneId)))
            {
                episodeByUuid.TryAdd(sceneUuids[episode.TpdbSceneId], episode);
            }

            // Created unmonitored, so the refresh it queues only fetches metadata; the v2 settings go on at the end
            var studio = _studioService.FindByForeignId(siteUuid) ?? _addStudioService.AddStudio(new Studio
            {
                ForeignId = siteUuid,
                Title = series.Title,
                Monitored = false,
                MoviesMonitored = false,
                SearchOnAdd = false,
                RootFolderPath = series.RootFolderPath,
                QualityProfileId = series.QualityProfileId,
                Tags = ParseTags(series.Tags)
            });

            Movie BuildStub(string uuid)
            {
                return new Movie
                {
                    ForeignId = uuid,
                    Path = series.Path,
                    RootFolderPath = series.RootFolderPath,
                    QualityProfileId = series.QualityProfileId,
                    Tags = ParseTags(series.Tags),
                    AddOptions = new AddMovieOptions { SearchForMovie = false, AddMethod = AddMovieMethod.Studio },
                    Monitored = episodeByUuid.TryGetValue(uuid, out var episode) ? episode.Monitored : series.MonitorNewItems == 0
                };
            }

            _catalogImporter.ImportScenes(series.Title, cursor => _movieInfo.GetStudioScenePage(siteUuid, cursor), BuildStub, () => true);

            var missing = episodeByUuid.Keys.Where(uuid => _movieService.FindByForeignId(uuid) == null).ToList();

            if (missing.Count > 0)
            {
                _addMovieService.AddMovies(missing.Select(BuildStub).ToList(), true, refreshInline: true);
            }

            foreach (var file in files)
            {
                LinkFile(series, file, episodes, sceneUuids, progress);
            }

            studio = _studioService.FindByForeignId(siteUuid) ?? studio;
            studio.Monitored = series.Monitored;
            studio.WhisparrMonitorNewItems = series.MonitorNewItems == 0;
            studio.SearchOnAdd = false;
            _studioService.Update(studio);

            MarkSeriesDone(series, episodes, progress);
        }

        // Null when TPDB doesn't know the site
        private string ResolveSite(LegacyV2Series series)
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

        private Dictionary<int, string> ResolveScenes(List<LegacyV2Episode> episodes)
        {
            var ids = episodes.Where(e => e.TpdbSceneId > 0).Select(e => e.TpdbSceneId).Distinct().ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<int, string>();
            }

            var result = _idResolver.Resolve(TpdbIdKind.Scene, ids);

            if (result.Throttled)
            {
                throw new ResolveThrottledException("scenes");
            }

            if (result.Pending)
            {
                throw new ResolvePendingException("scenes");
            }

            return result.Ids;
        }

        private void LinkFile(LegacyV2Series series, LegacyV2EpisodeFile file, List<LegacyV2Episode> episodes, Dictionary<int, string> sceneUuids, ILegacyImportProgress progress)
        {
            try
            {
                var fileEpisodes = episodes.Where(e => e.LegacyEpisodeFileId == file.LegacyId).OrderBy(e => e.LegacyId).ToList();
                var target = fileEpisodes.FirstOrDefault(e => e.TpdbSceneId > 0 && sceneUuids.ContainsKey(e.TpdbSceneId));
                var movie = target == null ? null : _movieService.FindByForeignId(sceneUuids[target.TpdbSceneId]);

                if (movie == null)
                {
                    _fileLinker.Park(file.Path, series.QualityProfileId, FileArea, "No TPDB scene link in v2");
                }
                else
                {
                    ReportOutcome(file, target, _fileLinker.Link(movie, file.Path, f => Fill(f, file)));

                    var others = fileEpisodes.Where(e => e != target).ToList();

                    if (others.Count > 0)
                    {
                        var unlinked = others.Where(e => e.TpdbSceneId <= 0 || !sceneUuids.ContainsKey(e.TpdbSceneId)).ToList();
                        var detail = $"Multi-scene file linked to {target.Title}; also v2 scenes: {string.Join(", ", others.Select(e => e.Title))}";

                        if (unlinked.Count > 0)
                        {
                            detail += $"; skipped without a TPDB scene link: {string.Join(", ", unlinked.Select(e => e.Title))}";
                        }

                        _migrationService.Report(LegacyReportCategory.NeedsAttention, FileArea, file.Path, detail);
                    }
                }
            }
            catch (Exception ex) when (!IsTransient(ex))
            {
                _logger.Warn(ex, "Could not link {0}", file.Path);

                _migrationService.Report(LegacyReportCategory.NeedsAttention, FileArea, file.Path, ex.Message);
            }

            MarkFileDone(file, progress);
        }

        private void ReportOutcome(LegacyV2EpisodeFile file, LegacyV2Episode target, LinkOutcome outcome)
        {
            var detail = outcome switch
            {
                LinkOutcome.LinkedElsewhere => $"Already linked to another scene; v2 had TPDB scene {target.TpdbSceneId}",
                LinkOutcome.Missing => "File not found",
                LinkOutcome.IsDirectory => "v2 stored no file name",
                LinkOutcome.OutsideRootFolders => "File is outside every root folder",
                _ => null
            };

            if (detail != null)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, FileArea, file.Path, detail);
            }
        }

        private static void Fill(MovieFile movieFile, LegacyV2EpisodeFile file)
        {
            movieFile.Size = file.Size;
            movieFile.Quality = string.IsNullOrWhiteSpace(file.Quality) ? new QualityModel() : QualityConverter.Parse(file.Quality);
            movieFile.Languages = string.IsNullOrWhiteSpace(file.Languages) ? new List<Language>() : LanguagesConverter.Parse(file.Languages);
            movieFile.ReleaseGroup = file.ReleaseGroup;
            movieFile.SceneName = file.SceneName;
            movieFile.OriginalFilePath = file.OriginalFilePath;
            movieFile.IndexerFlags = (IndexerFlags)file.IndexerFlags;
            movieFile.DateAdded = file.DateAdded;
        }

        // Parks the files of a series that can't be imported, then marks the series and its episodes done
        private void ParkAndFinish(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, string reason, ILegacyImportProgress progress)
        {
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

        private void MarkFileDone(LegacyV2EpisodeFile file, ILegacyImportProgress progress)
        {
            file.Done = true;
            _fileRepository.Update(file);
            progress.Advance(FileArea);
        }

        private void MarkSeriesDone(LegacyV2Series series, List<LegacyV2Episode> episodes, ILegacyImportProgress progress)
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
                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, ex.Message);
            }
            else
            {
                _seriesRepository.Update(series);
            }

            return giveUp;
        }

        private static bool IsTransient(Exception ex)
        {
            return ex is HttpException or WebException or TimeoutException or ResolveThrottledException;
        }

        private static HashSet<int> ParseTags(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? new HashSet<int>() : TagsConverter.Parse(json) ?? new HashSet<int>();
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
}
