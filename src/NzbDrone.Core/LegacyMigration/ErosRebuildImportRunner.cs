using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using FluentValidation;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.LegacyMigration
{
    // Rebuilds an Eros library on a new scene source from the seeds phase 1 wrote: studios, then performers, then
    // scenes (with their files), then the movies, which the movie runner handles as it always did
    public class ErosRebuildImportRunner : ILegacyImportRunner
    {
        private const string StudioArea = "Studios";
        private const string PerformerArea = "Performers";
        private const string SceneArea = "Scenes";

        // The same converter the repositories use to read this column
        private static readonly EmbeddedDocumentConverter<MediaInfoModel> MediaInfoConverter = new();

        private readonly ErosSeedImportRunner _movieRunner;
        private readonly ILegacyStudioSeedRepository _studioSeeds;
        private readonly ILegacyPerformerSeedRepository _performerSeeds;
        private readonly ILegacySceneSeedRepository _sceneSeeds;
        private readonly ILegacyMovieSeedRepository _movieSeeds;
        private readonly ISceneSourceIdMapper _idMapper;
        private readonly ILegacyHashMatcher _hashMatcher;
        private readonly IAddStudioService _addStudioService;
        private readonly IStudioService _studioService;
        private readonly IAddPerformerService _addPerformerService;
        private readonly IPerformerService _performerService;
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IScenePageWalker _pageWalker;
        private readonly ISceneCatalogImporter _catalogImporter;
        private readonly IAddMovieService _addMovieService;
        private readonly IMovieService _movieService;
        private readonly IImportlFileService _importFileService;
        private readonly ILegacyImportFileLocator _importFileLocator;
        private readonly ILegacyFileLinker _fileLinker;
        private readonly ILegacyMigrationService _migrationService;
        private readonly IDiskProvider _diskProvider;
        private readonly ISkyhookPacer _pacer;
        private readonly Logger _logger;

        // Scenes each owner's catalog walks have added so far, for owners whose catalog is not whole yet
        private readonly Dictionary<string, int> _catalogAdded = new();

        // Consecutive restore passes that ended with a catalog incomplete, and whether the cap was reached. In memory:
        // no state column fits, and a restart only gives the catalogs a fresh run of passes. Cleared when the restore
        // succeeds, which is when the migration completes, and when a different migration starts.
        private int _incompletePasses;
        private bool _catalogsGivenUp;
        private DateTime? _catalogStateStartedAt;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public ErosRebuildImportRunner(ErosSeedImportRunner movieRunner,
                                       ILegacyStudioSeedRepository studioSeeds,
                                       ILegacyPerformerSeedRepository performerSeeds,
                                       ILegacySceneSeedRepository sceneSeeds,
                                       ILegacyMovieSeedRepository movieSeeds,
                                       ISceneSourceIdMapper idMapper,
                                       ILegacyHashMatcher hashMatcher,
                                       IAddStudioService addStudioService,
                                       IStudioService studioService,
                                       IAddPerformerService addPerformerService,
                                       IPerformerService performerService,
                                       IProvideMovieInfo movieInfo,
                                       IScenePageWalker pageWalker,
                                       ISceneCatalogImporter catalogImporter,
                                       IAddMovieService addMovieService,
                                       IMovieService movieService,
                                       IImportlFileService importFileService,
                                       ILegacyImportFileLocator importFileLocator,
                                       ILegacyFileLinker fileLinker,
                                       ILegacyMigrationService migrationService,
                                       IDiskProvider diskProvider,
                                       ISkyhookPacer pacer,
                                       Logger logger)
        {
            _movieRunner = movieRunner;
            _studioSeeds = studioSeeds;
            _performerSeeds = performerSeeds;
            _sceneSeeds = sceneSeeds;
            _movieSeeds = movieSeeds;
            _idMapper = idMapper;
            _hashMatcher = hashMatcher;
            _addStudioService = addStudioService;
            _studioService = studioService;
            _addPerformerService = addPerformerService;
            _performerService = performerService;
            _movieInfo = movieInfo;
            _pageWalker = pageWalker;
            _catalogImporter = catalogImporter;
            _addMovieService = addMovieService;
            _movieService = movieService;
            _importFileService = importFileService;
            _importFileLocator = importFileLocator;
            _fileLinker = fileLinker;
            _migrationService = migrationService;
            _diskProvider = diskProvider;
            _pacer = pacer;
            _logger = logger;
        }

        public bool Handles(LegacyMigrationState state)
        {
            return state.SourceKind == LegacyKind.Eros && state.Rebuild;
        }

        public LegacyImportPass RunPass(LegacyMigrationState state, ILegacyImportProgress progress, bool recordOutages)
        {
            var studios = _studioSeeds.All().ToList();
            var performers = _performerSeeds.All().ToList();
            var scenes = _sceneSeeds.All().ToList();

            // Nothing from an earlier migration in this process carries over
            if (_catalogStateStartedAt != state.StartedAt)
            {
                ResetCatalogState();
                _catalogStateStartedAt = state.StartedAt;
            }

            // Seeds resolved on an earlier pass count as done so the bar stays consistent across passes
            progress.Total(studios.Count + performers.Count + scenes.Count + _movieSeeds.All().Count());
            progress.PhaseTotal(StudioArea, studios.Count);
            progress.PhaseTotal(PerformerArea, performers.Count);
            progress.PhaseTotal(SceneArea, scenes.Count);

            AdvanceResolved(progress, StudioArea, studios);
            AdvanceResolved(progress, PerformerArea, performers);
            AdvanceResolved(progress, SceneArea, scenes);

            var now = _pacer.UtcNow;
            var pass = RunStage(StudioStage(state), studios, progress, recordOutages, now);

            pass = LegacyImportPass.Combine(pass, RunStage(PerformerStage(state), performers, progress, recordOutages, now));

            // A scene needs its studio's new id, so none starts until every studio is settled
            var scenePass = studios.Any(s => !s.Resolved)
                ? new LegacyImportPass { Pending = scenes.Count(s => !s.Resolved) }
                : RunScenes(state, scenes, progress, recordOutages, now);

            pass = LegacyImportPass.Combine(pass, scenePass);

            pass = LegacyImportPass.Combine(pass, _movieRunner.RunMovieSeeds(state, progress, recordOutages));

            return pass.Pending == 0 ? RestoreMonitoring(state, now, pass) : pass;
        }

        // Once every seed is placed the studios and performers get the monitoring they had. Safe to run again: it only
        // writes what differs. Only the entity a seed created is touched; one it merged into keeps its own.
        private LegacyImportPass RestoreMonitoring(LegacyMigrationState state, DateTime now, LegacyImportPass pass)
        {
            try
            {
                // Before any flag goes on: a monitored studio or performer refreshes its whole catalog, and every scene of
                // it not in the library yet would come in monitored and be grabbed
                ImportCatalogs(state);

                foreach (var seed in FirstPerTarget(_studioSeeds.All(), s => s.Id, s => s.NewForeignId))
                {
                    var studio = _studioService.FindByForeignId(seed.NewForeignId);

                    if (studio != null && CreatedByMigration(state, studio.Added) &&
                        (studio.Monitored != seed.Monitored || studio.MoviesMonitored != seed.MoviesMonitored || studio.WhisparrMonitorNewItems != seed.WhisparrMonitorNewItems))
                    {
                        studio.Monitored = seed.Monitored;
                        studio.MoviesMonitored = seed.MoviesMonitored;
                        studio.WhisparrMonitorNewItems = seed.WhisparrMonitorNewItems;
                        _studioService.Update(studio);
                    }
                }

                foreach (var seed in FirstPerTarget(_performerSeeds.All(), s => s.Id, s => s.NewForeignId))
                {
                    var performer = _performerService.FindByForeignId(seed.NewForeignId);

                    if (performer != null && CreatedByMigration(state, performer.Added) &&
                        (performer.Monitored != seed.Monitored || performer.MoviesMonitored != seed.MoviesMonitored || performer.WhisparrMonitorNewItems != seed.WhisparrMonitorNewItems))
                    {
                        performer.Monitored = seed.Monitored;
                        performer.MoviesMonitored = seed.MoviesMonitored;
                        performer.WhisparrMonitorNewItems = seed.WhisparrMonitorNewItems;
                        _performerService.Update(performer);
                    }
                }

                ResetCatalogState();
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _logger.Warn(ex, "Could not restore studio and performer monitoring, will retry");

                pass.Pending++;
                pass.WaitingRetry++;
                pass.NextDue = LegacyRetryPolicy.Earlier(pass.NextDue, now + LegacyRetryPolicy.DelayBefore(1));
            }

            return pass;
        }

        // The catalogs of the studios and performers about to be monitored, added unmonitored. Scenes already in the
        // library are left as they are. An owner whose catalog is in has nothing left to add, so a run again writes no
        // second report row, and one whose flags are already restored is not walked at all.
        private void ImportCatalogs(LegacyMigrationState state)
        {
            var incomplete = new List<IncompleteCatalog>();

            // Given up on at the cap: the rows are written and the catalogs are not walked again
            if (_catalogsGivenUp)
            {
                return;
            }

            foreach (var seed in FirstPerTarget(_studioSeeds.All(), s => s.Id, s => s.NewForeignId))
            {
                var studio = _studioService.FindByForeignId(seed.NewForeignId);

                if (studio == null || !seed.Monitored || studio.Monitored || !CreatedByMigration(state, studio.Added))
                {
                    continue;
                }

                var tags = V2SeriesImportRunnerBase.ParseTags(seed.Tags);
                var title = seed.Title.IsNotNullOrWhiteSpace() ? seed.Title : seed.LegacyForeignId;
                var imported = _catalogImporter.ImportScenes(
                    title,
                    cursor => _movieInfo.GetStudioScenePage(seed.NewForeignId, cursor),
                    id => UnmonitoredStub(id, seed.RootFolderPath, seed.QualityProfileId, tags, AddMovieMethod.Studio),
                    () => true);

                SettleCatalog(state, $"studio:{seed.NewForeignId}", title, "studio", imported, incomplete);
            }

            foreach (var seed in FirstPerTarget(_performerSeeds.All(), s => s.Id, s => s.NewForeignId))
            {
                var performer = _performerService.FindByForeignId(seed.NewForeignId);

                if (performer == null || !seed.Monitored || performer.Monitored || !CreatedByMigration(state, performer.Added))
                {
                    continue;
                }

                var tags = V2SeriesImportRunnerBase.ParseTags(seed.Tags);
                var name = seed.Name.IsNotNullOrWhiteSpace() ? seed.Name : seed.LegacyForeignId;
                var imported = _catalogImporter.ImportScenes(
                    name,
                    cursor => _movieInfo.GetPerformerScenePage(seed.NewForeignId, cursor),
                    id => UnmonitoredStub(id, seed.RootFolderPath, seed.QualityProfileId, tags, AddMovieMethod.Performer),
                    () => true);

                SettleCatalog(state, $"performer:{seed.NewForeignId}", name, "performer", imported, incomplete);
            }

            if (incomplete.Count > 0)
            {
                HoldOrGiveUp(state, incomplete);
            }
        }

        private void HoldOrGiveUp(LegacyMigrationState state, List<IncompleteCatalog> incomplete)
        {
            // After every owner has had its turn, so one gap does not hold the others' catalogs back a pass. Nothing is
            // restored: a scene on a page given up on would come in monitored on the first refresh and be grabbed.
            // Only up to the cap, though: a page that can never be fetched must not keep automation paused forever.
            // The walker has already retried inside the pass and a transient failure throws instead, so there is no
            // outage rule here: every pass that ends incomplete counts.
            if (++_incompletePasses < LegacyRetryPolicy.MaxAttempts)
            {
                throw new CatalogIncompleteException(string.Join(", ", incomplete.Select(c => c.Owner)), incomplete.Sum(c => c.GaveUp));
            }

            foreach (var catalog in incomplete)
            {
                _migrationService.Report(
                    LegacyReportCategory.NeedsAttention,
                    SceneArea,
                    catalog.Owner,
                    $"The catalog was incomplete ({catalog.GaveUp} page(s) not fetched); a later refresh may add the missing scenes using this {catalog.Kind}'s monitoring settings, so unmonitor it first if you do not want them grabbed");
                ReportLeftBehind(state, catalog.Owner, _catalogAdded.GetValueOrDefault(catalog.Key));
            }

            _catalogsGivenUp = true;
        }

        private void ResetCatalogState()
        {
            _catalogAdded.Clear();
            _incompletePasses = 0;
            _catalogsGivenUp = false;
        }

        private sealed record IncompleteCatalog(string Key, string Owner, string Kind, int GaveUp);

        // The left-behind row says how many scenes the migration added for the owner, once, when its catalog is whole.
        // A walk that gave up on pages remembers what it added so the walk that completes can count it too; the count
        // lives in memory, so a restart between the two walks leaves it short.
        private void SettleCatalog(LegacyMigrationState state, string key, string owner, string kind, SceneCatalogImportResult imported, List<IncompleteCatalog> incomplete)
        {
            _catalogAdded[key] = _catalogAdded.GetValueOrDefault(key) + imported.Added;

            if (imported.GaveUp > 0)
            {
                incomplete.Add(new IncompleteCatalog(key, owner, kind, imported.GaveUp));

                return;
            }

            ReportLeftBehind(state, owner, _catalogAdded[key]);
            _catalogAdded.Remove(key);
        }

        private static Movie UnmonitoredStub(string id, string rootFolderPath, int qualityProfileId, HashSet<int> tags, AddMovieMethod method)
        {
            return new Movie
            {
                ForeignId = id,
                Monitored = false,
                QualityProfileId = qualityProfileId,
                RootFolderPath = rootFolderPath,
                Tags = new HashSet<int>(tags),
                AddOptions = new AddMovieOptions { SearchForMovie = false, AddMethod = method }
            };
        }

        private void ReportLeftBehind(LegacyMigrationState state, string owner, int added)
        {
            if (added > 0)
            {
                _migrationService.Report(LegacyReportCategory.LeftBehind, SceneArea, owner, $"{SourceName(state.SceneSource)} scenes with no Eros twin, left unmonitored", added);
            }
        }

        // Seeds that merged into the same entity share its id; the first is the one that created it
        private static IEnumerable<T> FirstPerTarget<T>(IEnumerable<T> seeds, Func<T, int> id, Func<T, string> target)
        {
            return seeds.Where(s => target(s).IsNotNullOrWhiteSpace())
                        .OrderBy(id)
                        .GroupBy(target)
                        .Select(g => g.First());
        }

        // An entity older than the migration was already in the library, so the seed merged into it
        private static bool CreatedByMigration(LegacyMigrationState state, DateTime added)
        {
            return !state.StartedAt.HasValue || added >= state.StartedAt.Value;
        }

        private static void AdvanceResolved<T>(ILegacyImportProgress progress, string area, List<T> seeds)
            where T : ILegacySeed
        {
            var resolved = seeds.Count(s => s.Resolved);

            if (resolved > 0)
            {
                progress.Advance(area, resolved);
            }
        }

        private LegacyImportPass RunStage<T>(Stage<T> stage, List<T> seeds, ILegacyImportProgress progress, bool recordOutages, DateTime now)
            where T : ILegacySeed
        {
            var pass = new LegacyImportPass();
            var due = new List<T>();

            foreach (var seed in seeds.Where(s => !s.Resolved))
            {
                // A seed backing off from an earlier failure waits for its own turn
                if (LegacyRetryPolicy.IsDue(seed.Attempts, seed.LastAttemptAt, now))
                {
                    due.Add(seed);
                }
                else
                {
                    pass.Pending++;
                    pass.WaitingRetry++;
                    pass.NextDue = LegacyRetryPolicy.Earlier(pass.NextDue, LegacyRetryPolicy.DueAt(seed.Attempts, seed.LastAttemptAt));
                }
            }

            stage.Prepare?.Invoke(due);

            var failures = new List<(T Seed, Exception Error)>();
            var succeeded = 0;

            foreach (var seed in due)
            {
                pass.Attempted++;

                try
                {
                    stage.Resolve(seed);
                    progress.Advance(stage.Area);
                    succeeded++;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    _logger.Warn(ex, "Could not rebuild {0}, will retry", stage.Subject(seed));

                    failures.Add((seed, ex));
                }
                catch (Exception ex)
                {
                    // Retrying won't help; report it and move on so one bad seed can't hold the migration open
                    _logger.Warn(ex, "Could not rebuild {0}", stage.Subject(seed));

                    _migrationService.Report(LegacyReportCategory.NeedsAttention, stage.Area, stage.Subject(seed), ex.Message);

                    if (!seed.Resolved)
                    {
                        seed.Resolved = true;
                        stage.Update(seed);
                        progress.Advance(stage.Area);
                    }
                }
            }

            // Everything worked on failed transiently, so the source is down rather than the seeds: don't spend their attempts
            pass.Outage = failures.Count > 0 && succeeded == 0;

            SettleFailures(stage, failures, pass, progress, recordOutages);

            return pass;
        }

        private void SettleFailures<T>(Stage<T> stage, List<(T Seed, Exception Error)> failures, LegacyImportPass pass, ILegacyImportProgress progress, bool recordOutages)
            where T : ILegacySeed
        {
            foreach (var (seed, error) in failures)
            {
                if (pass.Outage && !recordOutages)
                {
                    pass.Pending++;
                    pass.WaitingRetry++;
                }
                else if (RecordFailure(stage, seed, error))
                {
                    progress.Advance(stage.Area);
                }
                else
                {
                    pass.Pending++;
                    pass.WaitingRetry++;
                    pass.NextDue = LegacyRetryPolicy.Earlier(pass.NextDue, LegacyRetryPolicy.DueAt(seed.Attempts, seed.LastAttemptAt));
                }
            }
        }

        private static bool IsTransient(Exception ex)
        {
            return ex is HttpException or WebException or TimeoutException or FingerprintTryLaterException or CatalogIncompleteException;
        }

        // Returns true when the seed was given up on
        private bool RecordFailure<T>(Stage<T> stage, T seed, Exception ex)
            where T : ILegacySeed
        {
            seed.Attempts++;
            seed.LastAttemptAt = _pacer.UtcNow;

            var giveUp = LegacyRetryPolicy.ShouldGiveUp(seed.Attempts);

            if (giveUp)
            {
                seed.Resolved = true;
                _migrationService.Report(LegacyReportCategory.NeedsAttention, stage.Area, stage.Subject(seed), $"{stage.LegacyDetail(seed)}: {ex.Message}");
            }

            stage.Update(seed);

            return giveUp;
        }

        private static string SourceName(SceneMetadataType source)
        {
            return source == SceneMetadataType.Tpdb ? "TPDB" : "StashDB";
        }

        private static SceneMetadataType PreviousSource(LegacyMigrationState state)
        {
            return state.SceneSource == SceneMetadataType.Tpdb ? SceneMetadataType.StashDb : SceneMetadataType.Tpdb;
        }

        // Seeds are from the other source, so their ids are too
        private static string LegacyIdDetail(LegacyMigrationState state, string legacyForeignId)
        {
            return $"Legacy {SourceName(PreviousSource(state))} id {legacyForeignId}";
        }

        // Stage: studios and performers

        private Stage<LegacyStudioSeed> StudioStage(LegacyMigrationState state)
        {
            return new Stage<LegacyStudioSeed>
            {
                Area = StudioArea,
                Subject = s => s.Title.IsNotNullOrWhiteSpace() ? s.Title : s.LegacyForeignId,
                LegacyDetail = s => LegacyIdDetail(state, s.LegacyForeignId),
                Update = s => _studioSeeds.Update(s),
                Resolve = s => ResolveStudio(state, s)
            };
        }

        private Stage<LegacyPerformerSeed> PerformerStage(LegacyMigrationState state)
        {
            return new Stage<LegacyPerformerSeed>
            {
                Area = PerformerArea,
                Subject = s => s.Name.IsNotNullOrWhiteSpace() ? s.Name : s.LegacyForeignId,
                LegacyDetail = s => LegacyIdDetail(state, s.LegacyForeignId),
                Update = s => _performerSeeds.Update(s),
                Resolve = s => ResolvePerformer(state, s)
            };
        }

        // Null when the new source has no such studio or performer. A seed that already carries its new id was started
        // on an earlier pass, so the id is not looked up again.
        private static string TargetId(LegacyMigrationState state, string newForeignId, string legacyTpdbId, Func<IdMapResult> map)
        {
            if (newForeignId.IsNotNullOrWhiteSpace())
            {
                return newForeignId;
            }

            if (state.SceneSource == SceneMetadataType.Tpdb && legacyTpdbId.IsNotNullOrWhiteSpace())
            {
                return legacyTpdbId;
            }

            return map().Id;
        }

        // The add services report an id the source doesn't have under this property; a cross-reference can be out of date
        private static bool IsIdNotFound(ValidationException ex)
        {
            return ex.Errors.Any(e => e.PropertyName == "StashId");
        }

        private static string UnmappedDetail(string kind, LegacyMigrationState state, IdMapResult mapping)
        {
            var source = SourceName(state.SceneSource);

            return mapping?.Method == IdMapMethod.Ambiguous
                ? $"More than one {kind} on {source} has this name"
                : $"{char.ToUpperInvariant(kind[0])}{kind[1..]} not found on {source}";
        }

        private void ResolveStudio(LegacyMigrationState state, LegacyStudioSeed seed)
        {
            var tags = V2SeriesImportRunnerBase.ParseTags(seed.Tags);
            var resumed = seed.NewForeignId.IsNotNullOrWhiteSpace();
            IdMapResult mapping = null;
            var targetId = TargetId(state, seed.NewForeignId, seed.LegacyTpdbId, () => mapping = _idMapper.MapStudio(PreviousSource(state), seed.LegacyForeignId, seed.Title));

            if (targetId == null)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, seed.Title ?? seed.LegacyForeignId, UnmappedDetail("studio", state, mapping));
                MarkResolved(seed);

                return;
            }

            // Kept before the add, so a pass that dies half way finds its own studio again instead of reporting a merge
            seed.NewForeignId = targetId;
            _studioSeeds.Update(seed);

            var existing = _studioService.FindByForeignId(targetId);

            if (existing != null && !resumed)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, seed.Title ?? seed.LegacyForeignId, $"Merged with {existing.Title}");
                MarkResolved(seed);

                return;
            }

            // Created unmonitored, so the refresh it queues only fetches metadata; the settings go on afterwards
            Studio NewStudio(string id) => new()
            {
                ForeignId = id,
                Title = seed.Title,
                Monitored = false,
                MoviesMonitored = false,
                WhisparrMonitorNewItems = false,
                SearchOnAdd = false,
                RootFolderPath = seed.RootFolderPath,
                QualityProfileId = seed.QualityProfileId,
                Tags = tags
            };

            Studio studio;

            try
            {
                studio = existing ?? _addStudioService.AddStudio(NewStudio(targetId));
            }
            catch (ValidationException ex) when (IsIdNotFound(ex))
            {
                var found = _idMapper.FindStudio(state.SceneSource, seed.Title);

                if (found.Id == null || found.Id == targetId)
                {
                    seed.NewForeignId = null;
                    _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, seed.Title ?? seed.LegacyForeignId, UnmappedDetail("studio", state, found));
                    MarkResolved(seed);

                    return;
                }

                _logger.Info(ex, "{0} has no studio {1}, using {2} found by name for {3}", SourceName(state.SceneSource), targetId, found.Id, seed.Title);

                targetId = found.Id;
                seed.NewForeignId = targetId;
                _studioSeeds.Update(seed);

                existing = _studioService.FindByForeignId(targetId);

                if (existing != null)
                {
                    _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, seed.Title ?? seed.LegacyForeignId, $"Merged with {existing.Title}");
                    MarkResolved(seed);

                    return;
                }

                studio = _addStudioService.AddStudio(NewStudio(targetId));
            }

            studio = _studioService.FindByForeignId(targetId) ?? studio;

            // Monitoring waits for the end of the rebuild: a monitored studio would import its whole catalog now
            studio.AfterDate = seed.AfterDate;
            studio.RootFolderPath = seed.RootFolderPath;
            studio.QualityProfileId = seed.QualityProfileId;
            studio.Tags = tags;
            studio.SearchOnAdd = false;
            _studioService.Update(studio);

            MarkResolved(seed);
        }

        private void ResolvePerformer(LegacyMigrationState state, LegacyPerformerSeed seed)
        {
            var tags = V2SeriesImportRunnerBase.ParseTags(seed.Tags);
            var resumed = seed.NewForeignId.IsNotNullOrWhiteSpace();
            IdMapResult mapping = null;
            var targetId = TargetId(state, seed.NewForeignId, seed.LegacyTpdbId, () => mapping = _idMapper.MapPerformer(PreviousSource(state), seed.LegacyForeignId, seed.Name));

            if (targetId == null)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, PerformerArea, seed.Name ?? seed.LegacyForeignId, UnmappedDetail("performer", state, mapping));
                MarkResolved(seed);

                return;
            }

            seed.NewForeignId = targetId;
            _performerSeeds.Update(seed);

            var existing = _performerService.FindByForeignId(targetId);

            // A hidden performer only came in through a scene's credits: adding takes it on and shows it
            var isMerge = existing is { Visibility: not PerformerVisibility.Hidden };

            if (isMerge && !resumed)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, PerformerArea, seed.Name ?? seed.LegacyForeignId, $"Merged with {existing.Name}");
                MarkResolved(seed);

                return;
            }

            Performer NewPerformer(string id) => new()
            {
                ForeignId = id,
                Name = seed.Name,
                Monitored = false,
                MoviesMonitored = false,
                WhisparrMonitorNewItems = false,
                SearchOnAdd = false,
                RootFolderPath = seed.RootFolderPath,
                QualityProfileId = seed.QualityProfileId,
                Tags = tags
            };

            Performer performer;

            try
            {
                performer = isMerge ? existing : _addPerformerService.AddPerformer(NewPerformer(targetId));
            }
            catch (ValidationException ex) when (IsIdNotFound(ex))
            {
                var found = _idMapper.FindPerformer(state.SceneSource, seed.Name);

                if (found.Id == null || found.Id == targetId)
                {
                    seed.NewForeignId = null;
                    _migrationService.Report(LegacyReportCategory.NeedsAttention, PerformerArea, seed.Name ?? seed.LegacyForeignId, UnmappedDetail("performer", state, found));
                    MarkResolved(seed);

                    return;
                }

                _logger.Info(ex, "{0} has no performer {1}, using {2} found by name for {3}", SourceName(state.SceneSource), targetId, found.Id, seed.Name);

                targetId = found.Id;
                seed.NewForeignId = targetId;
                _performerSeeds.Update(seed);

                existing = _performerService.FindByForeignId(targetId);

                if (existing is { Visibility: not PerformerVisibility.Hidden })
                {
                    _migrationService.Report(LegacyReportCategory.NeedsAttention, PerformerArea, seed.Name ?? seed.LegacyForeignId, $"Merged with {existing.Name}");
                    MarkResolved(seed);

                    return;
                }

                performer = _addPerformerService.AddPerformer(NewPerformer(targetId));
            }

            performer = _performerService.FindByForeignId(targetId) ?? performer;

            performer.AfterDate = seed.AfterDate;
            performer.RootFolderPath = seed.RootFolderPath;
            performer.QualityProfileId = seed.QualityProfileId;
            performer.Tags = tags;
            performer.SearchOnAdd = false;
            _performerService.Update(performer);

            MarkResolved(seed);
        }

        private void MarkResolved(LegacyStudioSeed seed)
        {
            seed.Resolved = true;
            _studioSeeds.Update(seed);
        }

        private void MarkResolved(LegacyPerformerSeed seed)
        {
            seed.Resolved = true;
            _performerSeeds.Update(seed);
        }

        private void MarkResolved(LegacySceneSeed seed)
        {
            seed.Resolved = true;
            _sceneSeeds.Update(seed);
        }

        // Stage: scenes

        private LegacyImportPass RunScenes(LegacyMigrationState state, List<LegacySceneSeed> scenes, ILegacyImportProgress progress, bool recordOutages, DateTime now)
        {
            var studioIds = _studioSeeds.All()
                .Where(s => s.LegacyForeignId.IsNotNullOrWhiteSpace())
                .GroupBy(s => s.LegacyForeignId)
                .ToDictionary(g => g.Key, g => g.First().NewForeignId);
            var context = new SceneContext(studioIds);

            var stage = new Stage<LegacySceneSeed>
            {
                Area = SceneArea,
                Subject = SceneSubject,
                LegacyDetail = s => LegacyIdDetail(state, s.LegacyForeignId),
                Update = s => _sceneSeeds.Update(s),
                Prepare = due => HashFiles(due, context, progress),
                Resolve = s => ResolveScene(state, s, context)
            };

            return RunStage(stage, scenes, progress, recordOutages, now);
        }

        private static string SceneSubject(LegacySceneSeed seed)
        {
            return seed.ReleaseDate.HasValue
                ? $"{seed.Title} ({seed.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"
                : seed.Title;
        }

        // One lookup for every scene with a file. A failed lookup is each of those scenes' own transient failure.
        private void HashFiles(List<LegacySceneSeed> due, SceneContext context, ILegacyImportProgress progress)
        {
            var queries = new List<HashQuery>();

            foreach (var seed in due.Where(s => s.ImportFileId.HasValue))
            {
                var file = _importFileLocator.Locate(ErosSeedImportRunner.FindImportFile(_importFileService, seed.ImportFileId.Value));

                if (file == null)
                {
                    continue;
                }

                context.Files[seed.Id] = file;
                queries.Add(new HashQuery(seed.Id.ToString(CultureInfo.InvariantCulture), file.Path, file.OsHash));
            }

            if (queries.Count == 0)
            {
                return;
            }

            try
            {
                context.Hashes = _hashMatcher.Match(queries, progress.Status);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _logger.Warn(ex, "The fingerprint lookup failed, will retry");

                context.HashError = ex;

                return;
            }

            var hashed = new List<ImportFile>();

            foreach (var (seedId, file) in context.Files)
            {
                if (context.Hashes.TryGetValue(seedId.ToString(CultureInfo.InvariantCulture), out var match) && match.OsHash.IsNotNullOrWhiteSpace() && match.OsHash != file.OsHash)
                {
                    file.OsHash = match.OsHash;
                    file.HashedFileModifiedAt = _diskProvider.FileGetLastWrite(file.Path);
                    hashed.Add(file);
                }
            }

            if (hashed.Count > 0)
            {
                _importFileService.SaveOsHashes(hashed);
            }
        }

        private void ResolveScene(LegacyMigrationState state, LegacySceneSeed seed, SceneContext context)
        {
            context.Files.TryGetValue(seed.Id, out var importFile);

            var sceneId = HashMatch(seed, importFile, context);
            var tier = sceneId != null ? MatchTier.Hash : MatchTier.Confirmed;

            var studioId = seed.LegacyStudioForeignId.IsNotNullOrWhiteSpace() ? context.StudioIds.GetValueOrDefault(seed.LegacyStudioForeignId) : null;

            if (sceneId == null && studioId.IsNotNullOrWhiteSpace())
            {
                sceneId = LegacySceneMatcher.FindExact(Catalog(studioId, context), seed.Title, seed.ReleaseDate);
            }

            if (sceneId == null)
            {
                // The file stays parked in the import list
                _migrationService.Report(LegacyReportCategory.NeedsAttention, SceneArea, SceneSubject(seed), LegacyIdDetail(state, seed.LegacyForeignId));
                MarkResolved(seed);

                return;
            }

            var tags = V2SeriesImportRunnerBase.ParseTags(seed.Tags);
            var movie = _movieService.FindByForeignId(sceneId);

            if (movie == null)
            {
                var stub = new Movie
                {
                    ForeignId = sceneId,
                    Monitored = seed.Monitored,
                    QualityProfileId = seed.QualityProfileId,
                    RootFolderPath = seed.RootFolderPath,
                    Tags = tags,
                    Path = seed.Path,
                    AddOptions = new AddMovieOptions { SearchForMovie = false }
                };

                // Inline, so no refresh queued by the add is still running with a stale movie when the file is linked.
                // Errors are not ignored: a failed add is the seed's failure, not an empty result.
                movie = _addMovieService.AddMovies(new List<Movie> { stub }, false, refreshInline: true).FirstOrDefault()
                    ?? _movieService.FindByForeignId(sceneId)
                    ?? throw new InvalidOperationException($"{SceneSubject(seed)} was not added");
            }

            if (seed.ImportFileId.HasValue)
            {
                LinkImportFile(seed, movie, importFile, tier);
            }

            MarkResolved(seed);
        }

        // Null when the scene has no file, the file could not be hashed, or the hash names no single scene
        private static string HashMatch(LegacySceneSeed seed, ImportFile importFile, SceneContext context)
        {
            if (importFile == null)
            {
                return null;
            }

            if (context.HashError != null)
            {
                throw context.HashError;
            }

            if (context.Hashes == null || !context.Hashes.TryGetValue(seed.Id.ToString(CultureInfo.InvariantCulture), out var match))
            {
                return null;
            }

            if (match.TryLater)
            {
                throw new FingerprintTryLaterException($"The fingerprint lookup asked to try {SceneSubject(seed)} again later");
            }

            return match.SceneId;
        }

        // The studio's catalog, walked once per pass
        private List<SceneKey> Catalog(string studioId, SceneContext context)
        {
            if (context.Catalogs.TryGetValue(studioId, out var cached))
            {
                return cached;
            }

            var keys = new List<SceneKey>();
            var result = _pageWalker.Walk(
                cursor => _movieInfo.GetStudioScenePage(studioId, cursor),
                page => keys.AddRange((page.Items ?? new List<ScenePageItem>()).Select(i => new SceneKey(i.SceneId, i.Title, i.ReleaseDate))),
                () => true);

            // A catalog missing pages would turn into false "not found" verdicts, so it is tried again later
            if (result.GaveUp.Count > 0)
            {
                throw new TimeoutException($"The scene catalog of studio {studioId} was not complete");
            }

            context.Catalogs[studioId] = keys;

            return keys;
        }

        private void LinkImportFile(LegacySceneSeed seed, Movie movie, ImportFile importFile, MatchTier tier)
        {
            if (importFile == null)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, SceneArea, SceneSubject(seed), "Parked file no longer exists");

                return;
            }

            var outcome = _fileLinker.Link(movie, importFile.Path, file => FillFromSeed(file, seed, _diskProvider.GetFileSize(importFile.Path)), tier);

            if (outcome == LinkOutcome.Linked || outcome == LinkOutcome.AlreadyLinked)
            {
                _importFileService.Remove(importFile.Id);

                return;
            }

            // The file stays parked in the import list either way
            if (outcome == LinkOutcome.SceneHasOtherFile)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, SceneArea, importFile.Path, $"Scene already has a file; legacy also had {importFile.Path}");

                return;
            }

            _migrationService.ReportUnlinkedFile(movie.Id, SceneArea, importFile.Path, $"File could not be linked: {outcome}");
        }

        // The file as Eros had it, through the same converters the repositories read the columns with
        internal static void FillFromSeed(MovieFile movieFile, LegacySceneSeed seed, long sizeOnDisk)
        {
            movieFile.Size = seed.FileSize > 0 ? seed.FileSize : sizeOnDisk;
            movieFile.Quality = V2SeriesImportRunnerBase.ParseQuality(seed.FileQuality);
            movieFile.Languages = V2SeriesImportRunnerBase.ParseLanguages(seed.FileLanguages);
            movieFile.MediaInfo = seed.FileMediaInfo.IsNullOrWhiteSpace() ? null : MediaInfoConverter.Parse(seed.FileMediaInfo);
            movieFile.ReleaseGroup = seed.FileReleaseGroup;
            movieFile.SceneName = seed.FileSceneName;
            movieFile.OriginalFilePath = seed.FileOriginalFilePath;
            movieFile.IndexerFlags = (IndexerFlags)seed.FileIndexerFlags;
            movieFile.Edition = seed.FileEdition;
            movieFile.DateAdded = seed.FileDateAdded ?? DateTime.UtcNow;
        }

        // What one pass over the scenes shares: the files and their hash verdicts, and the catalogs walked so far
        private sealed class SceneContext
        {
            public SceneContext(Dictionary<string, string> studioIds)
            {
                StudioIds = studioIds;
            }

            public Dictionary<string, string> StudioIds { get; }
            public Dictionary<int, ImportFile> Files { get; } = new();
            public Dictionary<string, HashMatch> Hashes { get; set; }
            public Exception HashError { get; set; }
            public Dictionary<string, List<SceneKey>> Catalogs { get; } = new();
        }

        // How one kind of seed is worked: Resolve either finishes the seed or throws
        private sealed class Stage<T>
        {
            public string Area { get; init; }
            public Func<T, string> Subject { get; init; }
            public Func<T, string> LegacyDetail { get; init; }
            public Action<T> Update { get; init; }
            public Action<T> Resolve { get; init; }
            public Action<List<T>> Prepare { get; init; }
        }
    }
}
