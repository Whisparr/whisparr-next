using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class ErosRebuildImportRunnerFixture : CoreTest<ErosRebuildImportRunner>
    {
        private const string Root = "/data/library";
        private const string LegacyScene = "legacy-scene-";

        private LegacyMigrationState _state;
        private List<LegacyStudioSeed> _studioSeeds;
        private List<LegacyPerformerSeed> _performerSeeds;
        private List<LegacySceneSeed> _sceneSeeds;
        private List<LegacyMovieSeed> _movieSeeds;
        private Dictionary<string, Studio> _studios;
        private Dictionary<string, Performer> _performers;
        private Dictionary<string, Movie> _movies;
        private Dictionary<string, IdMapResult> _studioMap;
        private Dictionary<string, IdMapResult> _performerMap;
        private Dictionary<string, HashMatch> _hashes;
        private Dictionary<string, List<ScenePageItem>> _catalogs;
        private Dictionary<int, ImportFile> _importFiles;
        private List<Studio> _studioAdds;
        private List<Performer> _performerAdds;
        private List<Studio> _studioUpdates;
        private List<Performer> _performerUpdates;
        private List<string> _order;
        private ScenePageWalkResult _walkResult;
        private Mock<ILegacyImportProgress> _progress;
        private Action<MovieFile> _fill;
        private DateTime _now;
        private int _nextId;

        [SetUp]
        public void Setup()
        {
            _state = new LegacyMigrationState
            {
                SourceKind = LegacyKind.Eros,
                Rebuild = true,
                SceneSource = SceneMetadataType.Tpdb,
                MovieSource = MovieMetadataType.TPDB
            };
            _studioSeeds = new List<LegacyStudioSeed>();
            _performerSeeds = new List<LegacyPerformerSeed>();
            _sceneSeeds = new List<LegacySceneSeed>();
            _movieSeeds = new List<LegacyMovieSeed>();
            _studios = new Dictionary<string, Studio>();
            _performers = new Dictionary<string, Performer>();
            _movies = new Dictionary<string, Movie>();
            _studioMap = new Dictionary<string, IdMapResult>();
            _performerMap = new Dictionary<string, IdMapResult>();
            _hashes = new Dictionary<string, HashMatch>();
            _catalogs = new Dictionary<string, List<ScenePageItem>>();
            _importFiles = new Dictionary<int, ImportFile>();
            _studioAdds = new List<Studio>();
            _performerAdds = new List<Performer>();
            _studioUpdates = new List<Studio>();
            _performerUpdates = new List<Performer>();
            _order = new List<string>();
            _walkResult = new ScenePageWalkResult();
            _progress = new Mock<ILegacyImportProgress>();
            _fill = null;
            _now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
            _nextId = 100;
            _state.StartedAt = _now.AddHours(-1);

            Mocker.GetMock<ISkyhookPacer>().SetupGet(p => p.UtcNow).Returns(() => _now);

            Mocker.GetMock<ILegacyStudioSeedRepository>().Setup(r => r.All()).Returns(() => _studioSeeds);
            Mocker.GetMock<ILegacyPerformerSeedRepository>().Setup(r => r.All()).Returns(() => _performerSeeds);
            Mocker.GetMock<ILegacySceneSeedRepository>().Setup(r => r.All()).Returns(() => _sceneSeeds);
            Mocker.GetMock<ILegacyMovieSeedRepository>().Setup(r => r.All()).Returns(() => _movieSeeds);

            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns<SceneMetadataType, string, string>((_, id, _) => _studioMap.GetValueOrDefault(id) ?? new IdMapResult(null, IdMapMethod.None));
            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapPerformer(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns<SceneMetadataType, string, string>((_, id, _) => _performerMap.GetValueOrDefault(id) ?? new IdMapResult(null, IdMapMethod.None));

            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                  .Returns<string>(id => _studios.GetValueOrDefault(id));
            Mocker.GetMock<IAddStudioService>()
                  .Setup(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()))
                  .Returns<Studio, bool>((studio, _) =>
                  {
                      // The settings are applied to this same instance afterwards, so what it was added with is kept apart
                      _studioAdds.Add(new Studio { ForeignId = studio.ForeignId, Title = studio.Title, Monitored = studio.Monitored, MoviesMonitored = studio.MoviesMonitored, SearchOnAdd = studio.SearchOnAdd });
                      studio.Added = _now;
                      _studios[studio.ForeignId] = studio;
                      _order.Add("studio");

                      return studio;
                  });
            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.Update(It.IsAny<Studio>()))
                  .Callback<Studio>(s => _studioUpdates.Add(new Studio { ForeignId = s.ForeignId, Monitored = s.Monitored, MoviesMonitored = s.MoviesMonitored, WhisparrMonitorNewItems = s.WhisparrMonitorNewItems, AfterDate = s.AfterDate, RootFolderPath = s.RootFolderPath, QualityProfileId = s.QualityProfileId, Tags = s.Tags }))
                  .Returns<Studio>(s => s);

            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                  .Returns<string>(id => _performers.GetValueOrDefault(id));
            Mocker.GetMock<IAddPerformerService>()
                  .Setup(s => s.AddPerformer(It.IsAny<Performer>(), It.IsAny<bool>()))
                  .Returns<Performer, bool>((performer, _) =>
                  {
                      _performerAdds.Add(new Performer { ForeignId = performer.ForeignId, Monitored = performer.Monitored, MoviesMonitored = performer.MoviesMonitored, SearchOnAdd = performer.SearchOnAdd });
                      performer.Added = _now;
                      _performers[performer.ForeignId] = performer;

                      return performer;
                  });
            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.Update(It.IsAny<Performer>()))
                  .Callback<Performer>(p => _performerUpdates.Add(new Performer { ForeignId = p.ForeignId, Monitored = p.Monitored, MoviesMonitored = p.MoviesMonitored, WhisparrMonitorNewItems = p.WhisparrMonitorNewItems, AfterDate = p.AfterDate, RootFolderPath = p.RootFolderPath, QualityProfileId = p.QualityProfileId, Tags = p.Tags }))
                  .Returns<Performer>(p => p);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                  .Returns<string>(id => _movies.GetValueOrDefault(id));
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns<List<Movie>, bool, bool>((movies, _, _) =>
                  {
                      foreach (var movie in movies)
                      {
                          movie.Id = _nextId++;
                          _movies[movie.ForeignId] = movie;
                          _order.Add(movie.ForeignId);
                      }

                      return movies;
                  });

            Mocker.GetMock<IScenePageWalker>()
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>(), It.IsAny<Func<bool>>()))
                  .Returns<Func<string, ScenePage>, Action<ScenePage>, Func<bool>>((fetch, handle, _) =>
                  {
                      handle(fetch(null));

                      return _walkResult;
                  });
            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(i => i.GetStudioScenePage(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns<string, string>((id, _) => new ScenePage { Items = _catalogs.GetValueOrDefault(id) ?? new List<ScenePageItem>() });

            Mocker.GetMock<ILegacyHashMatcher>()
                  .Setup(m => m.Match(It.IsAny<IReadOnlyCollection<HashQuery>>()))
                  .Returns<IReadOnlyCollection<HashQuery>>(queries => queries.ToDictionary(q => q.Key, q => _hashes.GetValueOrDefault(q.Path) ?? new HashMatch(null, null, false)));

            // The real service throws for a row that is gone
            Mocker.GetMock<IImportlFileService>()
                  .Setup(s => s.Get(It.IsAny<int>()))
                  .Returns<int>(id => _importFiles.TryGetValue(id, out var file) ? file : throw new ModelNotFoundException(typeof(ImportFile), id));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(It.IsAny<string>())).Returns(1234);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Callback<Movie, string, Action<MovieFile>, MatchTier>((_, _, fill, _) => _fill = fill)
                  .Returns(LinkOutcome.Linked);

            // The movie seeds go through the real movie runner, on the same mocks
            Mocker.Resolve<ErosSeedImportRunner>();
        }

        private LegacyStudioSeed GivenStudioSeed(string legacyId, string title = null, string tpdbId = null)
        {
            var seed = new LegacyStudioSeed
            {
                Id = _studioSeeds.Count + 1,
                LegacyForeignId = legacyId,
                LegacyTpdbId = tpdbId,
                Title = title ?? $"Studio {legacyId}",
                Monitored = true,
                MoviesMonitored = true,
                WhisparrMonitorNewItems = false,
                AfterDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RootFolderPath = Root,
                QualityProfileId = 4,
                Tags = "[2,3]"
            };

            _studioSeeds.Add(seed);
            _studioMap[legacyId] = new IdMapResult("new-" + legacyId, IdMapMethod.Mapping);

            return seed;
        }

        private LegacyPerformerSeed GivenPerformerSeed(string legacyId, string name = null, string tpdbId = null)
        {
            var seed = new LegacyPerformerSeed
            {
                Id = _performerSeeds.Count + 1,
                LegacyForeignId = legacyId,
                LegacyTpdbId = tpdbId,
                Name = name ?? $"Performer {legacyId}",
                Monitored = true,
                MoviesMonitored = false,
                WhisparrMonitorNewItems = true,
                RootFolderPath = Root,
                QualityProfileId = 4,
                Tags = "[5]"
            };

            _performerSeeds.Add(seed);
            _performerMap[legacyId] = new IdMapResult("new-" + legacyId, IdMapMethod.Mapping);

            return seed;
        }

        private LegacySceneSeed GivenSceneSeed(string studioLegacyId = null, int? fileId = null, string title = "Big Scene")
        {
            var number = _sceneSeeds.Count + 1;
            var seed = new LegacySceneSeed
            {
                Id = number,
                LegacyForeignId = LegacyScene + number,
                Title = title,
                ReleaseDate = new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                LegacyStudioForeignId = studioLegacyId,
                Monitored = false,
                QualityProfileId = 4,
                Path = $"{Root}/Studio/Big Scene".AsOsAgnostic(),
                RootFolderPath = Root.AsOsAgnostic(),
                Tags = "[2]",
                ImportFileId = fileId
            };

            if (fileId.HasValue)
            {
                var path = $"{Root}/Studio/scene-{number}.mp4".AsOsAgnostic();

                _importFiles[fileId.Value] = new ImportFile { Id = fileId.Value, Path = path };
                seed.FileSize = 4321;
                seed.FileQuality = "{\"quality\":9,\"revision\":{\"version\":1}}";
                seed.FileMediaInfo = "{\"videoFormat\":\"h264\",\"width\":1920}";
                seed.FileReleaseGroup = "GRP";
                seed.FileSceneName = "Big.Scene.1080p";
                seed.FileOriginalFilePath = "orig/scene.mp4";
                seed.FileEdition = "Uncut";
                seed.FileDateAdded = new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc);
            }

            _sceneSeeds.Add(seed);

            return seed;
        }

        private string FilePath(LegacySceneSeed seed)
        {
            return _importFiles[seed.ImportFileId.Value].Path;
        }

        private void GivenCatalog(string studioId, params ScenePageItem[] items)
        {
            _catalogs[studioId] = items.ToList();
        }

        private static ScenePageItem CatalogItem(string id, string title, DateTime? date)
        {
            return new ScenePageItem { SceneId = id, Title = title, ReleaseDate = date, PerformerIds = new List<string>() };
        }

        private LegacyImportPass Run(bool recordOutages = false)
        {
            return Subject.RunPass(_state, _progress.Object, recordOutages);
        }

        private void VerifyReport(string area, string subject, string detail, Times times)
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.NeedsAttention, area, subject, detail, 1), times);
        }

        private void VerifyNoReports()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        private void VerifyMoviesAdded(Times times)
        {
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()), times);
        }

        [Test]
        public void handles_only_rebuild()
        {
            Subject.Handles(_state).Should().BeTrue();

            _state.Rebuild = false;
            Subject.Handles(_state).Should().BeFalse();

            _state.Rebuild = true;
            _state.SourceKind = LegacyKind.V2;
            Subject.Handles(_state).Should().BeFalse();
        }

        [Test]
        public void studio_uses_stored_tpdb_id_without_calling_mapper()
        {
            var seed = GivenStudioSeed("stash-s1", tpdbId: "tpdb-s1");

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _studioAdds.Select(s => s.ForeignId).Should().Equal("tpdb-s1");
            seed.NewForeignId.Should().Be("tpdb-s1");
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void studio_maps_from_the_previous_source_when_the_target_is_stashdb()
        {
            _state.SceneSource = SceneMetadataType.StashDb;
            var seed = GivenStudioSeed("tpdb-old", tpdbId: "tpdb-old");

            Run();

            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(SceneMetadataType.Tpdb, "tpdb-old", "Studio tpdb-old"), Times.Once());
            seed.NewForeignId.Should().Be("new-tpdb-old");
        }

        [Test]
        public void studio_mapped_from_stashdb_when_the_target_is_tpdb_and_no_stored_id()
        {
            var seed = GivenStudioSeed("stash-s1");

            Run();

            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(SceneMetadataType.StashDb, "stash-s1", "Studio stash-s1"), Times.Once());
            seed.NewForeignId.Should().Be("new-stash-s1");
        }

        [Test]
        public void studio_unmapped_reported()
        {
            var seed = GivenStudioSeed("stash-s1", "Lost Studio");
            _studioMap.Clear();

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyReport("Studios", "Lost Studio", "Studio not found on TPDB", Times.Once());
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
            seed.Resolved.Should().BeTrue();
            seed.NewForeignId.Should().BeNull();
        }

        [Test]
        public void studio_added_without_catalog_then_settings_applied()
        {
            GivenStudioSeed("stash-s1");

            Run();

            _studioAdds.Should().ContainSingle();
            _studioAdds[0].ForeignId.Should().Be("new-stash-s1");
            _studioAdds[0].Monitored.Should().BeFalse();
            _studioAdds[0].MoviesMonitored.Should().BeFalse();
            _studioAdds[0].SearchOnAdd.Should().BeFalse();

            // The settings go on unmonitored; the monitoring comes in the final stage of the same pass
            _studioUpdates.Should().HaveCount(2);
            _studioUpdates[0].ForeignId.Should().Be("new-stash-s1");
            _studioUpdates[0].Monitored.Should().BeFalse();
            _studioUpdates[0].MoviesMonitored.Should().BeFalse();
            _studioUpdates[0].WhisparrMonitorNewItems.Should().BeFalse();
            _studioUpdates[0].AfterDate.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            _studioUpdates[0].RootFolderPath.Should().Be(Root);
            _studioUpdates[0].QualityProfileId.Should().Be(4);
            _studioUpdates[0].Tags.Should().BeEquivalentTo(new[] { 2, 3 });
            _studioUpdates[1].Monitored.Should().BeTrue();
            _studioUpdates[1].MoviesMonitored.Should().BeTrue();
            _studioUpdates[1].WhisparrMonitorNewItems.Should().BeFalse();

            Mocker.GetMock<ISceneCatalogImporter>()
                  .Verify(i => i.ImportScenes(It.IsAny<string>(), It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()), Times.Never());
            Mocker.GetMock<IProvideMovieInfo>().Verify(i => i.GetStudioScenePage(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            VerifyMoviesAdded(Times.Never());
        }

        [Test]
        public void two_seeds_one_studio_merge_reported()
        {
            var first = GivenStudioSeed("stash-a", "Studio One");
            var second = GivenStudioSeed("stash-b", "Studio Uno");
            _studioMap["stash-b"] = _studioMap["stash-a"];

            var pass = Run();

            pass.Pending.Should().Be(0);
            _studioAdds.Should().ContainSingle();

            // One apply at add time and one restore, both for the studio the first seed created
            _studioUpdates.Should().HaveCount(2);
            _studios["new-stash-a"].Monitored.Should().BeTrue();
            VerifyReport("Studios", "Studio Uno", "Merged with Studio One", Times.Once());
            first.Resolved.Should().BeTrue();
            second.Resolved.Should().BeTrue();
            second.NewForeignId.Should().Be("new-stash-a");
        }

        [Test]
        public void studio_already_in_the_library_is_merged_and_keeps_its_settings()
        {
            _studios["new-stash-s1"] = new Studio { ForeignId = "new-stash-s1", Title = "Existing", Monitored = false };
            GivenStudioSeed("stash-s1");

            Run();

            VerifyReport("Studios", "Studio stash-s1", "Merged with Existing", Times.Once());
            _studios["new-stash-s1"].Monitored.Should().BeFalse();
            _studioUpdates.Should().BeEmpty();
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void monitoring_waits_until_every_scene_seed_is_resolved()
        {
            GivenStudioSeed("stash-s1");
            GivenPerformerSeed("stash-p1");
            var seed = GivenSceneSeed("stash-s1", fileId: 5);
            GivenCatalog("new-stash-s1", CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Throws(new TimeoutException());

            var pass = Run();

            pass.Pending.Should().Be(1);
            _studios["new-stash-s1"].Monitored.Should().BeFalse();
            _studios["new-stash-s1"].MoviesMonitored.Should().BeFalse();
            _studios["new-stash-s1"].WhisparrMonitorNewItems.Should().BeFalse();
            _performers["new-stash-p1"].Monitored.Should().BeFalse();
            _performers["new-stash-p1"].WhisparrMonitorNewItems.Should().BeFalse();
            _studioUpdates.Should().OnlyContain(s => !s.Monitored && !s.MoviesMonitored && !s.WhisparrMonitorNewItems);

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns(LinkOutcome.Linked);

            Run().Pending.Should().Be(0);

            _studios["new-stash-s1"].Monitored.Should().BeTrue();
            _studios["new-stash-s1"].MoviesMonitored.Should().BeTrue();
            _studios["new-stash-s1"].WhisparrMonitorNewItems.Should().BeFalse();
            _performers["new-stash-p1"].Monitored.Should().BeTrue();
            _performers["new-stash-p1"].MoviesMonitored.Should().BeFalse();
            _performers["new-stash-p1"].WhisparrMonitorNewItems.Should().BeTrue();
        }

        [Test]
        public void rerunning_the_final_stage_is_a_no_op()
        {
            GivenStudioSeed("stash-s1");
            GivenPerformerSeed("stash-p1");

            Run();

            var studioUpdates = _studioUpdates.Count;
            var performerUpdates = _performerUpdates.Count;

            Run().Pending.Should().Be(0);

            _studioUpdates.Should().HaveCount(studioUpdates);
            _performerUpdates.Should().HaveCount(performerUpdates);
        }

        [Test]
        public void merged_seeds_do_not_overwrite_existing_monitoring()
        {
            _studios["new-stash-s1"] = new Studio { ForeignId = "new-stash-s1", Title = "Existing", Added = _now.AddDays(-30), Monitored = false };
            _performers["new-stash-p1"] = new Performer { ForeignId = "new-stash-p1", Name = "Existing", Added = _now.AddDays(-30), Monitored = false };
            GivenStudioSeed("stash-s1");
            GivenPerformerSeed("stash-p1");

            Run().Pending.Should().Be(0);

            _studios["new-stash-s1"].Monitored.Should().BeFalse();
            _performers["new-stash-p1"].Monitored.Should().BeFalse();
            _studioUpdates.Should().BeEmpty();
            _performerUpdates.Should().BeEmpty();
        }

        [Test]
        public void studio_started_on_an_earlier_pass_is_not_reported_as_a_merge()
        {
            var seed = GivenStudioSeed("stash-s1");
            seed.NewForeignId = "new-stash-s1";
            _studios["new-stash-s1"] = new Studio { ForeignId = "new-stash-s1", Title = "Studio stash-s1", Added = _now };

            Run();

            VerifyNoReports();
            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _studios["new-stash-s1"].Monitored.Should().BeTrue();
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void studio_with_malformed_tags_is_reported_and_resolved()
        {
            var seed = GivenStudioSeed("stash-s1");
            seed.Tags = "not json";

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Studios", "Studio stash-s1", It.IsAny<string>(), 1), Times.Once());
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void studio_without_a_title_is_reported_by_its_legacy_id()
        {
            var seed = GivenStudioSeed("stash-s1");
            seed.Title = null;
            _studioMap.Clear();

            Run();

            VerifyReport("Studios", "stash-s1", "Studio not found on TPDB", Times.Once());
        }

        [Test]
        public void performer_added_unmonitored_then_settings_applied()
        {
            var seed = GivenPerformerSeed("stash-p1", tpdbId: "tpdb-p1");

            Run();

            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapPerformer(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _performerAdds.Should().ContainSingle();
            _performerAdds[0].ForeignId.Should().Be("tpdb-p1");
            _performerAdds[0].Monitored.Should().BeFalse();
            _performerAdds[0].MoviesMonitored.Should().BeFalse();
            _performerAdds[0].SearchOnAdd.Should().BeFalse();
            _performerUpdates.Should().HaveCount(2);
            _performerUpdates[0].Monitored.Should().BeFalse();
            _performerUpdates[0].WhisparrMonitorNewItems.Should().BeFalse();
            _performerUpdates[0].RootFolderPath.Should().Be(Root);
            _performerUpdates[0].QualityProfileId.Should().Be(4);
            _performerUpdates[0].Tags.Should().BeEquivalentTo(new[] { 5 });
            _performerUpdates[1].Monitored.Should().BeTrue();
            _performerUpdates[1].MoviesMonitored.Should().BeFalse();
            _performerUpdates[1].WhisparrMonitorNewItems.Should().BeTrue();
            seed.NewForeignId.Should().Be("tpdb-p1");
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void performer_unmapped_reported()
        {
            GivenPerformerSeed("stash-p1", "Lost Performer");
            _performerMap.Clear();

            Run();

            VerifyReport("Performers", "Lost Performer", "Performer not found on TPDB", Times.Once());
            Mocker.GetMock<IAddPerformerService>().Verify(s => s.AddPerformer(It.IsAny<Performer>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void performer_already_in_the_library_is_merged()
        {
            _performers["new-stash-p1"] = new Performer { ForeignId = "new-stash-p1", Name = "Existing Name" };
            GivenPerformerSeed("stash-p1", "Seed Name");

            Run();

            VerifyReport("Performers", "Seed Name", "Merged with Existing Name", Times.Once());
            _performerUpdates.Should().BeEmpty();
        }

        [Test]
        public void hidden_performer_is_taken_on_rather_than_merged()
        {
            _performers["new-stash-p1"] = new Performer { ForeignId = "new-stash-p1", Name = "Hidden", Visibility = PerformerVisibility.Hidden };
            GivenPerformerSeed("stash-p1");

            Run();

            VerifyNoReports();
            Mocker.GetMock<IAddPerformerService>().Verify(s => s.AddPerformer(It.IsAny<Performer>(), It.IsAny<bool>()), Times.Once());
            _performers["new-stash-p1"].Monitored.Should().BeTrue();
        }

        [Test]
        public void scenes_wait_for_unresolved_studios()
        {
            GivenStudioSeed("stash-s1");
            GivenSceneSeed("stash-s1");
            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new TimeoutException());

            var pass = Run();

            pass.Pending.Should().Be(2);
            pass.WaitingRetry.Should().Be(1);
            _sceneSeeds[0].Resolved.Should().BeFalse();
            _sceneSeeds[0].Attempts.Should().Be(0);
            Mocker.GetMock<ILegacyHashMatcher>().Verify(m => m.Match(It.IsAny<IReadOnlyCollection<HashQuery>>()), Times.Never());
            Mocker.GetMock<IProvideMovieInfo>().Verify(i => i.GetStudioScenePage(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            VerifyMoviesAdded(Times.Never());
        }

        [Test]
        public void scene_linked_by_oshash_keeps_file_metadata()
        {
            var seed = GivenSceneSeed(fileId: 5);
            var path = FilePath(seed);
            _hashes[path] = new HashMatch("tpdb-scene-9", "abc123", false);

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<IAddMovieService>().Verify(
                s => s.AddMovies(It.Is<List<Movie>>(l => l.Count == 1 &&
                                                         l[0].ForeignId == "tpdb-scene-9" &&
                                                         !l[0].Monitored &&
                                                         l[0].QualityProfileId == 4 &&
                                                         l[0].RootFolderPath == seed.RootFolderPath &&
                                                         l[0].Tags.SetEquals(new[] { 2 }) &&
                                                         l[0].Path == seed.Path &&
                                                         !l[0].AddOptions.SearchForMovie),
                                 false,
                                 true),
                Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.ForeignId == "tpdb-scene-9"), path, It.IsAny<Action<MovieFile>>(), MatchTier.Hash), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.SaveOsHashes(It.Is<List<ImportFile>>(l => l.Count == 1 && l[0].Id == 5 && l[0].OsHash == "abc123" && l[0].HashedFileModifiedAt.HasValue)), Times.Once());
            seed.Resolved.Should().BeTrue();

            var file = new MovieFile();
            _fill(file);
            file.Size.Should().Be(4321);
            file.Quality.Quality.Should().Be(Quality.HDTV1080p);
            file.ReleaseGroup.Should().Be("GRP");
            file.SceneName.Should().Be("Big.Scene.1080p");
            file.OriginalFilePath.Should().Be("orig/scene.mp4");
            file.Edition.Should().Be("Uncut");
            file.DateAdded.Should().Be(new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc));
            file.MediaInfo.VideoFormat.Should().Be("h264");
            file.MediaInfo.Width.Should().Be(1920);
            file.Languages.Should().BeEmpty();
        }

        [Test]
        public void scene_file_without_stored_details_falls_back_to_the_disk_size_and_no_media_info()
        {
            var seed = GivenSceneSeed(fileId: 5);
            seed.FileSize = 0;
            seed.FileMediaInfo = " ";
            seed.FileDateAdded = null;
            _hashes[FilePath(seed)] = new HashMatch("tpdb-scene-9", null, false);

            Run();

            var file = new MovieFile();
            _fill(file);
            file.Size.Should().Be(1234);
            file.MediaInfo.Should().BeNull();
            file.DateAdded.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            Mocker.GetMock<IImportlFileService>().Verify(s => s.SaveOsHashes(It.IsAny<List<ImportFile>>()), Times.Never());
        }

        [Test]
        public void a_hash_already_stored_is_not_saved_again()
        {
            var seed = GivenSceneSeed(fileId: 5);
            _importFiles[5].OsHash = "abc123";
            _hashes[FilePath(seed)] = new HashMatch("tpdb-scene-9", "abc123", false);

            Run();

            Mocker.GetMock<IImportlFileService>().Verify(s => s.SaveOsHashes(It.IsAny<List<ImportFile>>()), Times.Never());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
        }

        [Test]
        public void scene_linked_by_catalog_title_and_date()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1", fileId: 5);
            GivenCatalog(
                "new-stash-s1",
                CatalogItem("scene-other", "Another Scene", seed.ReleaseDate),
                CatalogItem("scene-wrong-day", "Big Scene", seed.ReleaseDate.Value.AddDays(1)),
                CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.ForeignId == "scene-match"), FilePath(seed), It.IsAny<Action<MovieFile>>(), MatchTier.Confirmed), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
            VerifyNoReports();
        }

        [Test]
        public void the_studio_catalog_is_walked_once_for_all_its_scenes()
        {
            GivenStudioSeed("stash-s1");
            var first = GivenSceneSeed("stash-s1");
            var second = GivenSceneSeed("stash-s1", title: "Second Scene");
            GivenCatalog(
                "new-stash-s1",
                CatalogItem("scene-1", "Big Scene", first.ReleaseDate),
                CatalogItem("scene-2", "Second Scene", second.ReleaseDate));

            Run();

            Mocker.GetMock<IProvideMovieInfo>().Verify(i => i.GetStudioScenePage("new-stash-s1", null), Times.Once());
            _movies.Keys.Should().BeEquivalentTo("scene-1", "scene-2");
        }

        [Test]
        public void scene_of_a_studio_with_no_new_id_is_matched_by_hash_only()
        {
            GivenStudioSeed("stash-s1");
            _studioMap.Clear();
            var seed = GivenSceneSeed("stash-s1");

            Run();

            Mocker.GetMock<IProvideMovieInfo>().Verify(i => i.GetStudioScenePage(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            VerifyReport("Scenes", "Big Scene (2020-05-01)", "Legacy StashDB id legacy-scene-1", Times.Once());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void scene_without_file_added_monitored_as_seed()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1");
            seed.Monitored = true;
            GivenCatalog("new-stash-s1", CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));

            Run();

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(l => l[0].ForeignId == "scene-match" && l[0].Monitored), false, true), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void scene_unmatched_reported_and_file_stays_parked()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1", fileId: 5);
            GivenCatalog("new-stash-s1", CatalogItem("scene-other", "Another Scene", seed.ReleaseDate));

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyReport("Scenes", "Big Scene (2020-05-01)", "Legacy StashDB id legacy-scene-1", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
            VerifyMoviesAdded(Times.Never());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void scene_without_a_release_date_is_reported_by_title_alone()
        {
            var seed = GivenSceneSeed();
            seed.ReleaseDate = null;

            Run();

            VerifyReport("Scenes", "Big Scene", "Legacy StashDB id legacy-scene-1", Times.Once());
        }

        [Test]
        public void two_seeds_same_scene_second_reported()
        {
            var first = GivenSceneSeed(fileId: 5);
            var second = GivenSceneSeed(fileId: 6);
            _hashes[FilePath(first)] = new HashMatch("tpdb-scene-9", "h1", false);
            _hashes[FilePath(second)] = new HashMatch("tpdb-scene-9", "h2", false);
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), FilePath(second), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns(LinkOutcome.SceneHasOtherFile);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyMoviesAdded(Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(6), Times.Never());
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Scenes", FilePath(second), It.Is<string>(d => d.StartsWith("Scene already has a file") && d.Contains(FilePath(second))), 1), Times.Once());
            first.Resolved.Should().BeTrue();
            second.Resolved.Should().BeTrue();
        }

        [Test]
        public void other_link_outcome_keeps_the_file_and_reports()
        {
            var seed = GivenSceneSeed(fileId: 5);
            _hashes[FilePath(seed)] = new HashMatch("tpdb-scene-9", "h1", false);
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns(LinkOutcome.Missing);

            Run();

            VerifyReport("Scenes", FilePath(seed), "File could not be linked: Missing", Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void already_linked_file_is_removed_from_the_import_list()
        {
            var seed = GivenSceneSeed(fileId: 5);
            _hashes[FilePath(seed)] = new HashMatch("tpdb-scene-9", "h1", false);
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns(LinkOutcome.AlreadyLinked);

            Run();

            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
        }

        [Test]
        public void scene_whose_parked_file_is_gone_is_added_and_reported()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1", fileId: 5);
            GivenCatalog("new-stash-s1", CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));
            _importFiles.Clear();

            Run();

            VerifyMoviesAdded(Times.Once());
            VerifyReport("Scenes", "Big Scene (2020-05-01)", "Parked file no longer exists", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void a_gone_file_in_the_hash_batch_does_not_fail_the_pass_or_its_neighbours()
        {
            var gone = GivenSceneSeed(fileId: 5);
            var kept = GivenSceneSeed(fileId: 6);
            _hashes[FilePath(kept)] = new HashMatch("tpdb-scene-9", "h2", false);
            _importFiles.Remove(5);

            var pass = Run();

            pass.Pending.Should().Be(0);
            gone.Resolved.Should().BeTrue();
            kept.Resolved.Should().BeTrue();
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), FilePath(kept), It.IsAny<Action<MovieFile>>(), MatchTier.Hash), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(6), Times.Once());
        }

        [Test]
        public void a_gone_file_for_a_scene_already_in_the_library_is_reported_at_link_time()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1", fileId: 5);
            GivenCatalog("new-stash-s1", CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));
            _movies["scene-match"] = new Movie { Id = 50, ForeignId = "scene-match" };
            _importFiles.Remove(5);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyMoviesAdded(Times.Never());
            VerifyReport("Scenes", "Big Scene (2020-05-01)", "Parked file no longer exists", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void a_kill_between_removing_the_file_and_resolving_the_seed_resumes_cleanly()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1", fileId: 5);
            GivenCatalog("new-stash-s1", CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));

            // The first pass linked the file and removed its row, then died before the seed was marked resolved
            _movies["scene-match"] = new Movie { Id = 50, ForeignId = "scene-match" };
            _importFiles.Remove(5);

            var pass = Run();

            pass.Pending.Should().Be(0);
            seed.Resolved.Should().BeTrue();
            VerifyMoviesAdded(Times.Never());
        }

        [Test]
        public void scene_already_in_the_library_is_not_added_again()
        {
            var seed = GivenSceneSeed(fileId: 5);
            _hashes[FilePath(seed)] = new HashMatch("tpdb-scene-9", "h1", false);
            _movies["tpdb-scene-9"] = new Movie { Id = 50, ForeignId = "tpdb-scene-9" };

            Run();

            VerifyMoviesAdded(Times.Never());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.Id == 50), FilePath(seed), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Once());
        }

        [Test]
        public void add_that_returns_nothing_and_finds_nothing_is_reported()
        {
            var seed = GivenSceneSeed(fileId: 5);
            _hashes[FilePath(seed)] = new HashMatch("tpdb-scene-9", "h1", false);
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new List<Movie>());

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Scenes", "Big Scene (2020-05-01)", It.Is<string>(d => d.Contains("was not added")), 1), Times.Once());
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void try_later_from_the_hash_lookup_is_that_scenes_transient_failure()
        {
            var waiting = GivenSceneSeed(fileId: 5);
            var other = GivenSceneSeed(fileId: 6);
            _hashes[FilePath(waiting)] = new HashMatch(null, "h1", true);
            _hashes[FilePath(other)] = new HashMatch("tpdb-scene-9", "h2", false);

            var pass = Run();

            pass.Outage.Should().BeFalse();
            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            waiting.Resolved.Should().BeFalse();
            waiting.Attempts.Should().Be(1);
            other.Resolved.Should().BeTrue();
            Mocker.GetMock<IImportlFileService>().Verify(s => s.SaveOsHashes(It.Is<List<ImportFile>>(l => l.Count == 2)), Times.Once());
        }

        [Test]
        public void a_failed_hash_lookup_fails_every_scene_with_a_file_transiently()
        {
            var seed = GivenSceneSeed(fileId: 5);
            var noFile = GivenSceneSeed();
            Mocker.GetMock<ILegacyHashMatcher>()
                  .Setup(m => m.Match(It.IsAny<IReadOnlyCollection<HashQuery>>()))
                  .Throws(new TimeoutException("down"));

            var pass = Run();

            pass.Pending.Should().Be(1);
            seed.Resolved.Should().BeFalse();
            noFile.Resolved.Should().BeTrue();
            VerifyReport("Scenes", "Big Scene (2020-05-01)", "Legacy StashDB id legacy-scene-2", Times.Once());
        }

        [Test]
        public void incomplete_catalog_is_a_transient_failure_not_a_verdict()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1");
            _walkResult.GaveUp.Add("cursor:abc");

            var pass = Run();

            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            seed.Resolved.Should().BeFalse();
            VerifyNoReports();
        }

        [Test]
        public void non_transient_add_failure_resolves_the_scene_as_needs_attention()
        {
            var seed = GivenSceneSeed(fileId: 5);
            _hashes[FilePath(seed)] = new HashMatch("tpdb-scene-9", "h1", false);
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Throws(new FluentValidation.ValidationException("Path conflict"));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Scenes", "Big Scene (2020-05-01)", It.Is<string>(d => d.Contains("Path conflict")), 1), Times.Once());
            seed.Resolved.Should().BeTrue();
            seed.Attempts.Should().Be(0);
            _progress.Verify(p => p.Advance("Scenes", 1), Times.Once());
        }

        [Test]
        public void every_seed_failing_transiently_is_an_outage_that_spends_no_attempts()
        {
            var seed = GivenStudioSeed("stash-s1");
            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new TimeoutException());

            var pass = Run();

            pass.Outage.Should().BeTrue();
            pass.Attempted.Should().Be(1);
            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.NextDue.Should().BeNull();
            seed.Attempts.Should().Be(0);
        }

        [Test]
        public void outage_attempts_are_spent_when_the_pass_is_told_to_record_them()
        {
            var seed = GivenStudioSeed("stash-s1");
            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new TimeoutException());

            var pass = Run(recordOutages: true);

            pass.Outage.Should().BeTrue();
            pass.NextDue.Should().Be(_now.AddMinutes(5));
            seed.Attempts.Should().Be(1);
            seed.LastAttemptAt.Should().Be(_now);
        }

        [Test]
        public void isolated_transient_failure_records_an_attempt_and_stays_pending()
        {
            var failing = GivenPerformerSeed("stash-p1");
            var healthy = GivenPerformerSeed("stash-p2");
            failing.Attempts = 1;
            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapPerformer(It.IsAny<SceneMetadataType>(), "stash-p1", It.IsAny<string>()))
                  .Throws(new TimeoutException());

            var pass = Run();

            pass.Outage.Should().BeFalse();
            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.NextDue.Should().Be(_now.AddMinutes(30));
            failing.Attempts.Should().Be(2);
            failing.LastAttemptAt.Should().Be(_now);
            failing.Resolved.Should().BeFalse();
            healthy.Resolved.Should().BeTrue();
        }

        [Test]
        public void fifth_failure_gives_up_as_needs_attention_naming_the_legacy_id()
        {
            var failing = GivenStudioSeed("stash-s1", "Slow Studio");
            GivenStudioSeed("stash-s2");
            failing.Attempts = 4;
            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapStudio(It.IsAny<SceneMetadataType>(), "stash-s1", It.IsAny<string>()))
                  .Throws(new TimeoutException("slow"));

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyReport("Studios", "Slow Studio", "Legacy StashDB id stash-s1: slow", Times.Once());
            failing.Attempts.Should().Be(5);
            failing.Resolved.Should().BeTrue();
            _progress.Verify(p => p.Advance("Studios", 1), Times.Exactly(2));
        }

        [Test]
        public void a_seed_still_backing_off_is_skipped()
        {
            var seed = GivenStudioSeed("stash-s1");
            seed.Attempts = 1;
            seed.LastAttemptAt = _now.AddMinutes(-4);

            var pass = Run();

            pass.Attempted.Should().Be(0);
            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.NextDue.Should().Be(_now.AddMinutes(1));
            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void resolved_seeds_are_advanced_at_once_and_not_worked_again()
        {
            GivenStudioSeed("stash-s1").Resolved = true;
            GivenPerformerSeed("stash-p1").Resolved = true;
            GivenSceneSeed().Resolved = true;
            GivenSceneSeed().Resolved = true;

            var pass = Run();

            pass.Pending.Should().Be(0);
            pass.Attempted.Should().Be(0);
            _progress.Verify(p => p.Total(4), Times.Once());
            _progress.Verify(p => p.Advance("Studios", 1), Times.Once());
            _progress.Verify(p => p.Advance("Performers", 1), Times.Once());
            _progress.Verify(p => p.Advance("Scenes", 2), Times.Once());
            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void restart_between_stages_no_duplicates()
        {
            GivenStudioSeed("stash-s1");
            GivenPerformerSeed("stash-p1");
            var seed = GivenSceneSeed("stash-s1", fileId: 5);
            GivenCatalog("new-stash-s1", CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));

            // The link dies after the scene was added: the studio and performer are done, the scene is not
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Throws(new TimeoutException());

            var first = Run();

            first.Pending.Should().Be(1);
            seed.Resolved.Should().BeFalse();

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns(LinkOutcome.Linked);
            _now = _now.AddMinutes(10);

            var restarted = Mocker.Container.New<ErosRebuildImportRunner>();
            var second = restarted.RunPass(_state, _progress.Object, false);

            second.Pending.Should().Be(0);
            _studioAdds.Should().ContainSingle();
            Mocker.GetMock<IAddPerformerService>().Verify(s => s.AddPerformer(It.IsAny<Performer>(), It.IsAny<bool>()), Times.Once());
            VerifyMoviesAdded(Times.Once());
            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(It.IsAny<SceneMetadataType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.ForeignId == "scene-match"), FilePath(seed), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Exactly(2));
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
            VerifyNoReports();
            seed.Resolved.Should().BeTrue();
        }

        [Test]
        public void movie_seeds_run_last_and_combine()
        {
            GivenStudioSeed("stash-s1");
            var seed = GivenSceneSeed("stash-s1");
            GivenCatalog("new-stash-s1", CatalogItem("scene-match", "Big Scene", seed.ReleaseDate));
            _state.MovieSource = MovieMetadataType.TMDB;
            _movieSeeds.Add(new LegacyMovieSeed { Id = 1, LegacyForeignId = "legacy-movie", Title = "The Big Movie", Year = 2020, TmdbId = 123, RootFolderPath = Root });
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException());

            var pass = Run();

            _order.Should().Equal("studio", "scene-match");
            pass.Attempted.Should().Be(3);
            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.Outage.Should().BeFalse();
            _progress.Verify(p => p.Total(3), Times.Once());
            _progress.Verify(p => p.Advance("Studios", 1), Times.Once());
            _progress.Verify(p => p.Advance("Scenes", 1), Times.Once());
            _movieSeeds[0].Resolved.Should().BeFalse();
        }

        [Test]
        public void movie_seeds_alone_still_complete_the_pass()
        {
            _state.MovieSource = MovieMetadataType.TMDB;
            _movieSeeds.Add(new LegacyMovieSeed { Id = 1, LegacyForeignId = "legacy-movie", Title = "The Big Movie", Year = 2020, TmdbId = 123, RootFolderPath = Root });
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Returns(new List<Movie> { new() { TmdbId = 123, MovieMetadata = new MovieMetadata { Title = "The Big Movie", Year = 2020, TmdbId = 123, ForeignId = "new-123" } } });

            var pass = Run();

            pass.Pending.Should().Be(0);
            _order.Should().Equal("new-123");
            _movieSeeds[0].Resolved.Should().BeTrue();
        }

        [Test]
        public void combine_adds_counts_and_takes_the_earliest_due()
        {
            var early = new DateTime(2026, 10, 29, 12, 5, 0, DateTimeKind.Utc);
            var late = new DateTime(2026, 10, 29, 13, 0, 0, DateTimeKind.Utc);

            var combined = LegacyImportPass.Combine(
                new LegacyImportPass { Pending = 1, WaitingRetry = 2, Waiting = 3, Attempted = 4, NextDue = late },
                new LegacyImportPass { Pending = 10, WaitingRetry = 20, Waiting = 30, Attempted = 40, NextDue = early });

            combined.Pending.Should().Be(11);
            combined.WaitingRetry.Should().Be(22);
            combined.Waiting.Should().Be(33);
            combined.Attempted.Should().Be(44);
            combined.NextDue.Should().Be(early);

            LegacyImportPass.Combine(new LegacyImportPass(), new LegacyImportPass { NextDue = late }).NextDue.Should().Be(late);
            LegacyImportPass.Combine(new LegacyImportPass { NextDue = late }, new LegacyImportPass()).NextDue.Should().Be(late);
            LegacyImportPass.Combine(new LegacyImportPass(), new LegacyImportPass()).NextDue.Should().BeNull();
        }

        [TestCase(0, false, 0, false, false)]
        [TestCase(2, true, 0, false, true)]
        [TestCase(0, false, 3, true, true)]
        [TestCase(2, true, 3, true, true)]
        [TestCase(2, true, 3, false, false)]
        [TestCase(2, false, 3, true, false)]
        [TestCase(2, false, 0, false, false)]
        [TestCase(0, true, 0, true, false)]
        public void combine_outage_rules(int aAttempted, bool aOutage, int bAttempted, bool bOutage, bool expected)
        {
            var combined = LegacyImportPass.Combine(
                new LegacyImportPass { Attempted = aAttempted, Outage = aOutage },
                new LegacyImportPass { Attempted = bAttempted, Outage = bOutage });

            combined.Outage.Should().Be(expected);
        }
    }
}
