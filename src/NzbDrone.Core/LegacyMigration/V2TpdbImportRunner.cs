using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;

namespace NzbDrone.Core.LegacyMigration
{
    // Turns the staged v2 library into v5 studios with their full TPDB catalog, copies the monitored state,
    // and links each v2 file to its scene in place
    public class V2TpdbImportRunner : V2SeriesImportRunnerBase
    {
        private readonly IAddStudioService _addStudioService;
        private readonly ISceneCatalogImporter _catalogImporter;
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IAddMovieService _addMovieService;
        private readonly IMovieService _movieService;

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
            : base(seriesRepository, episodeRepository, fileRepository, idResolver, studioService, fileLinker, migrationService, pacer, logger)
        {
            _addStudioService = addStudioService;
            _catalogImporter = catalogImporter;
            _movieInfo = movieInfo;
            _addMovieService = addMovieService;
            _movieService = movieService;
        }

        public override bool Handles(LegacyMigrationState state)
        {
            return state.SourceKind == LegacyKind.V2 && state.SceneSource == SceneMetadataType.Tpdb;
        }

        protected override void Import(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, ILegacyImportProgress progress)
        {
            var siteUuid = ResolveSite(series);

            if (siteUuid == null)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, "Studio not found on TPDB");
                ParkAndFinish(series, episodes, files, "Studio not found on TPDB", progress);

                return;
            }

            CreatedStudioIds[series.LegacyId] = siteUuid;

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

            var imported = _catalogImporter.ImportScenes(series.Title, cursor => _movieInfo.GetStudioScenePage(siteUuid, cursor), BuildStub, () => true);

            var missing = episodeByUuid.Keys.Where(uuid => _movieService.FindByForeignId(uuid) == null).ToList();

            if (missing.Count > 0)
            {
                _addMovieService.AddMovies(missing.Select(BuildStub).ToList(), true, refreshInline: true);
            }

            foreach (var file in files)
            {
                LinkFile(series, file, episodes, sceneUuids, progress);
            }

            // Linked files are done and stay so. Finishing now would leave scenes of the pages given up on to the first
            // refresh, which adds them with the studio's monitoring settings
            if (imported.GaveUp > 0)
            {
                throw new CatalogIncompleteException(series.Title, imported.GaveUp);
            }

            ApplyV2Settings(series, _studioService.FindByForeignId(siteUuid) ?? studio);
            CreatedStudioIds.Remove(series.LegacyId);

            MarkSeriesDone(series, episodes, progress);
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
                    var outcome = _fileLinker.Link(movie, file.Path, f => Fill(f, file));

                    ReportOutcome(file, target, outcome);

                    if (outcome == LinkOutcome.SceneHasOtherFile)
                    {
                        _fileLinker.Park(file.Path, series.QualityProfileId, FileArea, "Scene already has a file");
                    }

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
                LinkOutcome.SceneHasOtherFile => $"Scene already has a file; v2 also had {file.Path}",
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
    }
}
