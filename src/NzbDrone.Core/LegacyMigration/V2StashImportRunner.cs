using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;

namespace NzbDrone.Core.LegacyMigration
{
    // Turns the staged v2 library into v5 studios on StashDB: the studio is mapped from its TPDB site, and each v2 scene
    // and file is identified against the StashDB catalog by title and date, by name, or by oshash
    public class V2StashImportRunner : V2SeriesImportRunnerBase
    {
        private const string NotIdentified = "Not identified on StashDB";

        private readonly ISceneSourceIdMapper _idMapper;
        private readonly ILegacyHashMatcher _hashMatcher;
        private readonly IAddStudioService _addStudioService;
        private readonly ISceneCatalogImporter _catalogImporter;
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IAddMovieService _addMovieService;
        private readonly IMovieService _movieService;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public V2StashImportRunner(ILegacyV2SeriesRepository seriesRepository,
                                   ILegacyV2EpisodeRepository episodeRepository,
                                   ILegacyV2EpisodeFileRepository fileRepository,
                                   ITpdbIdResolver idResolver,
                                   ISceneSourceIdMapper idMapper,
                                   ILegacyHashMatcher hashMatcher,
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
            _idMapper = idMapper;
            _hashMatcher = hashMatcher;
            _addStudioService = addStudioService;
            _catalogImporter = catalogImporter;
            _movieInfo = movieInfo;
            _addMovieService = addMovieService;
            _movieService = movieService;
        }

        public override bool Handles(LegacyMigrationState state)
        {
            return state.SourceKind == LegacyKind.V2 && state.SceneSource == SceneMetadataType.StashDb;
        }

        protected override void Import(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, ILegacyImportProgress progress)
        {
            var siteUuid = ResolveSite(series);
            var mapped = _idMapper.MapStudio(SceneMetadataType.Tpdb, siteUuid, series.Title);

            if (mapped.Id == null)
            {
                var reason = mapped.Method == IdMapMethod.Ambiguous
                    ? "More than one studio on StashDB has this name"
                    : "Studio not found on StashDB";

                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, reason);
                ParkAndFinish(series, episodes, files, reason, progress);

                return;
            }

            var stashId = mapped.Id;

            CreatedStudioIds[series.LegacyId] = stashId;

            // Created unmonitored, so the refresh it queues only fetches metadata; the v2 settings go on at the end
            var studio = _studioService.FindByForeignId(stashId) ?? _addStudioService.AddStudio(new Studio
            {
                ForeignId = stashId,
                Title = series.Title,
                Monitored = false,
                MoviesMonitored = false,
                SearchOnAdd = false,
                RootFolderPath = series.RootFolderPath,
                QualityProfileId = series.QualityProfileId,
                Tags = ParseTags(series.Tags)
            });

            Movie BuildStub(string id)
            {
                return new Movie
                {
                    ForeignId = id,
                    Path = series.Path,
                    RootFolderPath = series.RootFolderPath,
                    QualityProfileId = series.QualityProfileId,
                    Tags = ParseTags(series.Tags),
                    AddOptions = new AddMovieOptions { SearchForMovie = false, AddMethod = AddMovieMethod.Studio },
                    Monitored = false
                };
            }

            var imported = _catalogImporter.ImportScenes(series.Title, cursor => _movieInfo.GetStudioScenePage(stashId, cursor), BuildStub, () => true);

            var catalog = new Dictionary<string, Movie>();

            foreach (var scene in _movieService.GetByStudioForeignId(stashId))
            {
                catalog.TryAdd(scene.ForeignId, scene);
            }

            var keys = catalog.Values.Select(m => new SceneKey(m.ForeignId, m.Title, m.MovieMetadata.Value.ReleaseDateUtc)).ToList();
            var claimed = new HashSet<string>();
            var owners = files.ToDictionary(f => f, f => episodes.Where(e => e.LegacyEpisodeFileId == f.LegacyId).OrderBy(e => e.LegacyId).FirstOrDefault());

            var catalogComplete = imported.GaveUp == 0;

            LinkFiles(series, files, owners, new CatalogLink(stashId, catalog, keys, claimed, BuildStub, catalogComplete), progress);

            // Linked files are done and stay so. Finishing now would leave scenes of the pages given up on to the first
            // refresh, which adds them with the studio's monitoring settings
            if (!catalogComplete)
            {
                throw new CatalogIncompleteException(series.Title, imported.GaveUp);
            }

            // Whatever a pending file did not claim gets its twin by title and date alone. An episode whose file was
            // done on an earlier pass is not fileless: its file already took its scene, which may not be the one its
            // title names, so it is left out, and the scenes that have a file count as claimed.
            var ownerEpisodes = owners.Values.Where(e => e != null).ToHashSet();
            var pendingFileIds = files.Select(f => f.LegacyId).ToHashSet();
            var doneFileEpisodes = episodes.Where(e => e.LegacyEpisodeFileId != 0 && !pendingFileIds.Contains(e.LegacyEpisodeFileId)).ToHashSet();

            if (doneFileEpisodes.Count > 0)
            {
                claimed.UnionWith(catalog.Where(c => c.Value.HasFile).Select(c => c.Key));
            }

            foreach (var episode in episodes.Where(e => !ownerEpisodes.Contains(e) && !doneFileEpisodes.Contains(e)))
            {
                var id = LegacySceneMatcher.FindExact(keys, episode.Title, ParsedAirDate(episode));

                if (id == null)
                {
                    continue;
                }

                claimed.Add(id);
                MonitorIfWanted(catalog[id], episode);
            }

            var leftBehind = catalog.Keys.Count(id => !claimed.Contains(id));

            if (mapped.Method == IdMapMethod.Title)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention, StudioArea, series.Title, "Matched by title");
            }

            if (leftBehind > 0)
            {
                _migrationService.Report(LegacyReportCategory.LeftBehind, "Scenes", series.Title, "StashDB scenes with no v2 twin, left unmonitored", leftBehind);
            }

            ApplyV2Settings(series, _studioService.FindByForeignId(stashId) ?? studio);
            CreatedStudioIds.Remove(series.LegacyId);

            MarkSeriesDone(series, episodes, progress);
        }

        // What matching a series' files needs from the catalog; Claimed fills in as files take their scenes
        private sealed record CatalogLink(string StashId, Dictionary<string, Movie> Catalog, List<SceneKey> Keys, HashSet<string> Claimed, Func<string, Movie> BuildStub, bool Complete);

        private void LinkFiles(LegacyV2Series series,
                               List<LegacyV2EpisodeFile> files,
                               Dictionary<LegacyV2EpisodeFile, LegacyV2Episode> owners,
                               CatalogLink link,
                               ILegacyImportProgress progress)
        {
            var (stashId, catalog, keys, claimed, _, catalogComplete) = link;
            var unmatched = new List<LegacyV2EpisodeFile>();

            foreach (var file in files)
            {
                Movie movie;
                MatchTier tier;

                try
                {
                    (movie, tier) = MatchByMetadata(file, owners[file], stashId, catalog, keys);
                }
                catch (Exception ex) when (!IsTransient(ex))
                {
                    ReportFileFailure(file, ex, progress);

                    continue;
                }

                if (movie == null)
                {
                    unmatched.Add(file);
                }
                else
                {
                    LinkFile(series, file, owners[file], movie, tier, claimed, progress);
                }
            }

            // With pages of the catalog missing, a file's scene may be on one: it waits for the next pass rather than be
            // fingerprinted or parked as unidentified now
            if (unmatched.Count > 0 && catalogComplete)
            {
                LinkUnmatched(series, unmatched, owners, link, progress);
            }
        }

        private void LinkUnmatched(LegacyV2Series series,
                                   List<LegacyV2EpisodeFile> unmatched,
                                   Dictionary<LegacyV2EpisodeFile, LegacyV2Episode> owners,
                                   CatalogLink link,
                                   ILegacyImportProgress progress)
        {
            var (_, catalog, _, claimed, buildStub, _) = link;

            // One lookup for all the files left, so the whole series retries if the source asks for it
            var hashes = _hashMatcher.Match(
                unmatched.Select(f => new HashQuery(f.LegacyId.ToString(CultureInfo.InvariantCulture), f.Path, null)).ToList(),
                (step, remaining) => progress.Status(step, remaining));

            if (hashes.Values.Any(h => h.TryLater))
            {
                throw new FingerprintTryLaterException($"The fingerprint lookup asked to try {series.Title} again later");
            }

            var hashed = new Dictionary<LegacyV2EpisodeFile, string>();

            foreach (var file in unmatched)
            {
                if (hashes.TryGetValue(file.LegacyId.ToString(CultureInfo.InvariantCulture), out var hash) && hash.SceneId != null)
                {
                    hashed[file] = hash.SceneId;
                }
            }

            var scenes = ResolveHashedScenes(hashed.Values.Distinct().ToList(), catalog, buildStub);

            foreach (var file in unmatched)
            {
                if (hashed.TryGetValue(file, out var sceneId) && scenes.TryGetValue(sceneId, out var movie))
                {
                    LinkFile(series, file, owners[file], movie, MatchTier.Hash, claimed, progress);
                }
                else
                {
                    ParkUnmatched(series, file, progress);
                }
            }
        }

        // A title and date match is Confirmed; the file name alone only earns Name
        private (Movie Movie, MatchTier Tier) MatchByMetadata(LegacyV2EpisodeFile file, LegacyV2Episode owner, string stashId, Dictionary<string, Movie> catalog, List<SceneKey> keys)
        {
            if (owner != null)
            {
                var id = LegacySceneMatcher.FindExact(keys, owner.Title, ParsedAirDate(owner));

                if (id != null)
                {
                    return (catalog[id], MatchTier.Confirmed);
                }
            }

            var parsed = NzbDrone.Core.Parser.Parser.ParseMoviePath(file.Path);
            var found = parsed == null ? null : _movieService.FindScene(parsed);

            if (found == null || found.MovieMetadata.Value.StudioForeignId != stashId)
            {
                return (null, MatchTier.Unknown);
            }

            return (catalog.GetValueOrDefault(found.ForeignId) ?? found, MatchTier.Name);
        }

        // A scene the hash names may sit outside the studio's catalog; it is added so the file has somewhere to go
        private Dictionary<string, Movie> ResolveHashedScenes(List<string> sceneIds, Dictionary<string, Movie> catalog, Func<string, Movie> buildStub)
        {
            var scenes = new Dictionary<string, Movie>();
            var missing = new List<string>();

            foreach (var id in sceneIds)
            {
                var movie = catalog.GetValueOrDefault(id) ?? _movieService.FindByForeignId(id);

                if (movie == null)
                {
                    missing.Add(id);
                }
                else
                {
                    scenes[id] = movie;
                }
            }

            if (missing.Count > 0)
            {
                foreach (var added in _addMovieService.AddMovies(missing.Select(buildStub).ToList(), true, refreshInline: true))
                {
                    scenes[added.ForeignId] = added;
                }
            }

            return scenes;
        }

        private void LinkFile(LegacyV2Series series, LegacyV2EpisodeFile file, LegacyV2Episode owner, Movie movie, MatchTier tier, HashSet<string> claimed, ILegacyImportProgress progress)
        {
            try
            {
                claimed.Add(movie.ForeignId);

                var outcome = _fileLinker.Link(movie, file.Path, f => Fill(f, file), tier);

                ReportOutcome(file, outcome);

                // Other outcomes already put the file in the report
                if (tier == MatchTier.Name && outcome is LinkOutcome.Linked or LinkOutcome.AlreadyLinked)
                {
                    _migrationService.Report(LegacyReportCategory.NeedsAttention, FileArea, file.Path, "Matched by file name only; check it");
                }

                if (outcome == LinkOutcome.SceneHasOtherFile)
                {
                    _fileLinker.Park(file.Path, series.QualityProfileId, FileArea, "Scene already has a file");
                }

                if (outcome is LinkOutcome.Linked or LinkOutcome.AlreadyLinked && owner != null)
                {
                    MonitorIfWanted(movie, owner);
                }
            }
            catch (Exception ex) when (!IsTransient(ex))
            {
                ReportFileFailure(file, ex, progress);

                return;
            }

            MarkFileDone(file, progress);
        }

        private void ParkUnmatched(LegacyV2Series series, LegacyV2EpisodeFile file, ILegacyImportProgress progress)
        {
            try
            {
                _fileLinker.Park(file.Path, series.QualityProfileId, FileArea, NotIdentified);
            }
            catch (Exception ex) when (!IsTransient(ex))
            {
                ReportFileFailure(file, ex, progress);

                return;
            }

            MarkFileDone(file, progress);
        }

        private void ReportFileFailure(LegacyV2EpisodeFile file, Exception ex, ILegacyImportProgress progress)
        {
            _logger.Warn(ex, "Could not link {0}", file.Path);

            _migrationService.Report(LegacyReportCategory.NeedsAttention, FileArea, file.Path, ex.Message);
            MarkFileDone(file, progress);
        }

        private void MonitorIfWanted(Movie movie, LegacyV2Episode episode)
        {
            if (!episode.Monitored || movie.Monitored)
            {
                return;
            }

            movie.Monitored = true;
            _movieService.UpdateMovie(movie);
        }

        private void ReportOutcome(LegacyV2EpisodeFile file, LinkOutcome outcome)
        {
            var detail = outcome switch
            {
                LinkOutcome.LinkedElsewhere => "Already linked to another scene",
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

        private static DateTime? ParsedAirDate(LegacyV2Episode episode)
        {
            return DateTime.TryParse(episode.AirDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        }
    }
}
