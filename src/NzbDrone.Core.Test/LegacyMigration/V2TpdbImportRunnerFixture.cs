using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class V2TpdbImportRunnerFixture : CoreTest<V2TpdbImportRunner>
    {
        private const string Root = "/data";

        private LegacyMigrationState _state;
        private List<LegacyV2Series> _series;
        private List<LegacyV2Episode> _episodes;
        private List<LegacyV2EpisodeFile> _files;
        private Dictionary<int, string> _sites;
        private Dictionary<int, string> _scenes;
        private Dictionary<string, Studio> _studios;
        private Dictionary<string, Movie> _movies;
        private Mock<ILegacyImportProgress> _progress;
        private Func<string, Movie> _buildStub;
        private Action<MovieFile> _fill;
        private bool _sitePending;
        private bool _scenePending;
        private int _nextId;

        [SetUp]
        public void Setup()
        {
            _state = new LegacyMigrationState { SourceKind = LegacyKind.V2, SceneSource = SceneMetadataType.Tpdb };
            _series = new List<LegacyV2Series>();
            _episodes = new List<LegacyV2Episode>();
            _files = new List<LegacyV2EpisodeFile>();
            _sites = new Dictionary<int, string>();
            _scenes = new Dictionary<int, string>();
            _studios = new Dictionary<string, Studio>();
            _movies = new Dictionary<string, Movie>();
            _progress = new Mock<ILegacyImportProgress>();
            _sitePending = false;
            _scenePending = false;
            _nextId = 100;

            Mocker.GetMock<ILegacyV2SeriesRepository>().Setup(r => r.All()).Returns(() => _series);
            Mocker.GetMock<ILegacyV2EpisodeRepository>().Setup(r => r.All()).Returns(() => _episodes);
            Mocker.GetMock<ILegacyV2EpisodeFileRepository>().Setup(r => r.All()).Returns(() => _files);

            Mocker.GetMock<ITpdbIdResolver>()
                  .Setup(r => r.Resolve(TpdbIdKind.Site, It.IsAny<IReadOnlyCollection<int>>()))
                  .Returns<TpdbIdKind, IReadOnlyCollection<int>>((_, ids) => new TpdbResolveResult
                  {
                      Pending = _sitePending,
                      Ids = ids.Where(_sites.ContainsKey).ToDictionary(i => i, i => _sites[i])
                  });
            Mocker.GetMock<ITpdbIdResolver>()
                  .Setup(r => r.Resolve(TpdbIdKind.Scene, It.IsAny<IReadOnlyCollection<int>>()))
                  .Returns<TpdbIdKind, IReadOnlyCollection<int>>((_, ids) => new TpdbResolveResult
                  {
                      Pending = _scenePending,
                      Ids = ids.Where(_scenes.ContainsKey).ToDictionary(i => i, i => _scenes[i])
                  });

            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                  .Returns<string>(id => _studios.GetValueOrDefault(id));
            Mocker.GetMock<IAddStudioService>()
                  .Setup(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()))
                  .Returns<Studio, bool>((studio, _) =>
                  {
                      _studios[studio.ForeignId] = studio;
                      return studio;
                  });

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
                      }

                      return movies;
                  });

            Mocker.GetMock<ISceneCatalogImporter>()
                  .Setup(i => i.ImportScenes(It.IsAny<string>(), It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()))
                  .Callback<string, Func<string, ScenePage>, Func<string, Movie>, Func<bool>>((_, _, build, _) => _buildStub = build)
                  .Returns(0);

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()))
                  .Callback<Movie, string, Action<MovieFile>>((_, _, fill) => _fill = fill)
                  .Returns(LinkOutcome.Linked);
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Park(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);
        }

        private LegacyV2Series GivenSeries(int legacyId, int siteId, string uuid, string title = null, bool monitored = true, int monitorNewItems = 0)
        {
            var series = new LegacyV2Series
            {
                Id = legacyId + 1000,
                LegacyId = legacyId,
                TpdbSiteId = siteId,
                Title = title ?? $"Studio {legacyId}",
                Path = $"{Root}/Studio {legacyId}",
                RootFolderPath = Root,
                Monitored = monitored,
                MonitorNewItems = monitorNewItems,
                QualityProfileId = 4,
                Tags = "[2,3]"
            };

            _series.Add(series);

            if (uuid != null)
            {
                _sites[siteId] = uuid;
            }

            return series;
        }

        private LegacyV2Episode GivenEpisode(int legacyId, LegacyV2Series series, int sceneId, string uuid, bool monitored = true, int fileId = 0)
        {
            var episode = new LegacyV2Episode
            {
                Id = legacyId + 2000,
                LegacyId = legacyId,
                LegacySeriesId = series.LegacyId,
                TpdbSceneId = sceneId,
                Title = $"Scene {legacyId}",
                Monitored = monitored,
                LegacyEpisodeFileId = fileId
            };

            _episodes.Add(episode);

            if (uuid != null)
            {
                _scenes[sceneId] = uuid;
            }

            return episode;
        }

        private LegacyV2EpisodeFile GivenFile(int legacyId, LegacyV2Series series, string name = null)
        {
            var file = new LegacyV2EpisodeFile
            {
                Id = legacyId + 3000,
                LegacyId = legacyId,
                LegacySeriesId = series.LegacyId,
                Path = $"{series.Path}/{name ?? $"file{legacyId}.mp4"}",
                Size = 5555,
                Quality = "{\"quality\":9,\"revision\":{\"version\":1}}",
                Languages = "[1,2]",
                ReleaseGroup = "GRP",
                SceneName = "scene.name",
                OriginalFilePath = "/dl/scene.name.mp4",
                IndexerFlags = 8,
                DateAdded = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            };

            _files.Add(file);

            return file;
        }

        private LegacyImportPass Run()
        {
            return Subject.RunPass(_state, _progress.Object);
        }

        private void VerifyReport(LegacyReportCategory category, string area, string subject, string detail, Times times)
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(category, area, subject, detail, It.IsAny<int>()), times);
        }

        [Test]
        public void handles_only_v2_with_tpdb_scenes()
        {
            Subject.Handles(_state).Should().BeTrue();

            _state.SceneSource = SceneMetadataType.StashDb;
            Subject.Handles(_state).Should().BeFalse();

            _state.SceneSource = SceneMetadataType.Tpdb;
            _state.SourceKind = LegacyKind.Eros;
            Subject.Handles(_state).Should().BeFalse();
        }

        [Test]
        public void resolves_site_and_creates_studio_unmonitored_then_applies_v2_settings()
        {
            var series = GivenSeries(1, 11, "site-uuid", monitored: false, monitorNewItems: 1);
            GivenEpisode(1, series, 101, "scene-1");

            var added = new List<Studio>();

            Mocker.GetMock<IAddStudioService>()
                  .Setup(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()))
                  .Returns<Studio, bool>((studio, _) =>
                  {
                      // What the queued refresh sees: nothing to sync
                      added.Add(new Studio { Monitored = studio.Monitored, MoviesMonitored = studio.MoviesMonitored, SearchOnAdd = studio.SearchOnAdd });
                      _studios[studio.ForeignId] = studio;

                      return studio;
                  });

            var pass = Run();

            pass.Pending.Should().Be(0);

            // Asserted per property: a whole-object comparison also walks Added, and BasicRepositoryFixture registers a process-wide DateTime rule
            var seen = added.Should().ContainSingle().Subject;
            seen.Monitored.Should().BeFalse();
            seen.MoviesMonitored.Should().BeFalse();
            seen.SearchOnAdd.Should().BeFalse();

            var studio = _studios["site-uuid"];

            studio.ForeignId.Should().Be("site-uuid");
            studio.RootFolderPath.Should().Be(Root);
            studio.QualityProfileId.Should().Be(4);
            studio.Tags.Should().BeEquivalentTo(new[] { 2, 3 });
            studio.Monitored.Should().BeFalse();
            studio.WhisparrMonitorNewItems.Should().BeFalse();
            studio.SearchOnAdd.Should().BeFalse();

            Mocker.GetMock<IStudioService>().Verify(s => s.Update(studio), Times.Once());
            Mocker.GetMock<ILegacyV2SeriesRepository>().Verify(r => r.Update(It.Is<LegacyV2Series>(s => s.Done)), Times.Once());
            _episodes.Should().OnlyContain(e => e.Done);
            _progress.Verify(p => p.Total(1), Times.Once());
            _progress.Verify(p => p.Advance("Studios", 1), Times.Once());
        }

        [Test]
        public void catalog_stub_copies_episode_monitored_and_defaults_new_items_from_monitor_new_items()
        {
            var all = GivenSeries(1, 11, "site-1", monitorNewItems: 0);
            var none = GivenSeries(2, 12, "site-2", monitorNewItems: 1);

            GivenEpisode(1, all, 101, "monitored-scene", monitored: true);
            GivenEpisode(2, all, 102, "unmonitored-scene", monitored: false);

            Run();

            // The stub builder is the one handed to the importer for the last series; build per series for the rest
            var forAll = BuildStubsFor("site-1");

            forAll("monitored-scene").Monitored.Should().BeTrue();
            forAll("unmonitored-scene").Monitored.Should().BeFalse();
            forAll("not-in-v2").Monitored.Should().BeTrue();

            var forNone = BuildStubsFor("site-2");

            forNone("not-in-v2").Monitored.Should().BeFalse();
            none.Done.Should().BeTrue();
        }

        // Re-runs a single-series pass so the importer hands over that series' stub builder
        private Func<string, Movie> BuildStubsFor(string siteUuid)
        {
            foreach (var series in _series)
            {
                series.Done = _sites[series.TpdbSiteId] != siteUuid;
            }

            Run();

            return _buildStub;
        }

        [Test]
        public void stub_path_is_series_folder_and_never_searches()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            GivenEpisode(1, series, 101, "scene-1");

            Run();

            var stub = _buildStub("scene-1");

            stub.Path.Should().Be($"{Root}/Studio 1");
            stub.RootFolderPath.Should().Be(Root);
            stub.QualityProfileId.Should().Be(4);
            stub.Tags.Should().BeEquivalentTo(new[] { 2, 3 });
            stub.AddOptions.SearchForMovie.Should().BeFalse();
            stub.AddOptions.AddMethod.Should().Be(AddMovieMethod.Studio);
            stub.ForeignId.Should().Be("scene-1");
        }

        [Test]
        public void episode_missing_from_catalog_added_individually()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            GivenEpisode(1, series, 101, "in-catalog");
            GivenEpisode(2, series, 102, "not-in-catalog");

            _movies["in-catalog"] = new Movie { Id = 7, ForeignId = "in-catalog" };

            Run();

            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovies(It.Is<List<Movie>>(l => l.Count == 1 && l[0].ForeignId == "not-in-catalog" && l[0].Path == $"{Root}/Studio 1"), true, true), Times.Once());
        }

        [Test]
        public void file_links_to_episode_scene_in_place()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var file = GivenFile(5, series);
            GivenEpisode(1, series, 101, "scene-1", fileId: 5);

            var pass = Run();

            pass.Pending.Should().Be(0);
            file.Done.Should().BeTrue();
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.ForeignId == "scene-1"), file.Path, It.IsAny<Action<MovieFile>>()), Times.Once());
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(It.IsAny<LegacyReportCategory>(), "Files", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            _progress.Verify(p => p.Advance("Files", 1), Times.Once());

            var movieFile = new MovieFile();

            _fill(movieFile);

            movieFile.Size.Should().Be(5555);
            movieFile.Quality.Quality.Should().Be(Quality.HDTV1080p);
            movieFile.Quality.Revision.Version.Should().Be(1);
            movieFile.Languages.Select(l => l.Id).Should().Equal(1, 2);
            movieFile.ReleaseGroup.Should().Be("GRP");
            movieFile.SceneName.Should().Be("scene.name");
            movieFile.OriginalFilePath.Should().Be("/dl/scene.name.mp4");
            movieFile.IndexerFlags.Should().Be((IndexerFlags)8);
            movieFile.DateAdded.Should().Be(file.DateAdded);
        }

        [Test]
        public void link_outcomes_are_reported()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var elsewhere = GivenFile(1, series);
            var missing = GivenFile(2, series);
            var directory = GivenFile(3, series);
            var linked = GivenFile(4, series);

            GivenEpisode(1, series, 101, "s1", fileId: 1);
            GivenEpisode(2, series, 102, "s2", fileId: 2);
            GivenEpisode(3, series, 103, "s3", fileId: 3);
            GivenEpisode(4, series, 104, "s4", fileId: 4);

            var outcomes = new Dictionary<string, LinkOutcome>
            {
                [elsewhere.Path] = LinkOutcome.LinkedElsewhere,
                [missing.Path] = LinkOutcome.Missing,
                [directory.Path] = LinkOutcome.IsDirectory,
                [linked.Path] = LinkOutcome.AlreadyLinked
            };

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()))
                  .Returns<Movie, string, Action<MovieFile>>((_, path, _) => outcomes[path]);

            Run();

            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", elsewhere.Path, "Already linked to another scene; v2 had TPDB scene 101", Times.Once());
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", missing.Path, "File not found", Times.Once());
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", directory.Path, "v2 stored no file name", Times.Once());
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(It.IsAny<LegacyReportCategory>(), "Files", linked.Path, It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void tpdb_scene_id_zero_parks_file()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var file = GivenFile(5, series);
            GivenEpisode(1, series, 0, null, fileId: 5);

            Run();

            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(file.Path, 4, "Files", "No TPDB scene link in v2"), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()), Times.Never());
            file.Done.Should().BeTrue();
        }

        [Test]
        public void file_without_episode_parks()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var file = GivenFile(5, series);

            Run();

            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(file.Path, 4, "Files", "No TPDB scene link in v2"), Times.Once());
        }

        [Test]
        public void unknown_site_parks_all_files_and_reports_studio()
        {
            var series = GivenSeries(1, 11, null, title: "Lost Studio");
            var a = GivenFile(1, series);
            var b = GivenFile(2, series);
            GivenEpisode(1, series, 101, "s1", fileId: 1);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyReport(LegacyReportCategory.NeedsAttention, "Studios", "Lost Studio", "Studio not found on TPDB", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(a.Path, 4, "Files", "Studio not found on TPDB"), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(b.Path, 4, "Files", "Studio not found on TPDB"), Times.Once());
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
            series.Done.Should().BeTrue();
            _files.Should().OnlyContain(f => f.Done);
            _episodes.Should().OnlyContain(e => e.Done);
            _progress.Verify(p => p.Total(3), Times.Once());
        }

        [Test]
        public void site_id_zero_is_unknown_without_asking_the_resolver()
        {
            GivenSeries(1, 0, null);

            Run();

            Mocker.GetMock<ITpdbIdResolver>().Verify(r => r.Resolve(It.IsAny<TpdbIdKind>(), It.IsAny<IReadOnlyCollection<int>>()), Times.Never());
        }

        [Test]
        public void directory_file_row_reported_never_linked()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var file = GivenFile(5, series);
            GivenEpisode(1, series, 101, "scene-1", fileId: 5);

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), file.Path, It.IsAny<Action<MovieFile>>()))
                  .Returns(LinkOutcome.IsDirectory);

            Run();

            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", file.Path, "v2 stored no file name", Times.Once());
            file.Done.Should().BeTrue();
        }

        [Test]
        public void multi_episode_file_links_first_and_reports_rest()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var file = GivenFile(5, series);
            GivenEpisode(3, series, 103, "scene-3", fileId: 5);
            GivenEpisode(2, series, 102, "scene-2", fileId: 5);
            GivenEpisode(1, series, 0, null, fileId: 5);

            Run();

            // Scene 1 comes first but has no TPDB link, so the file goes to Scene 2 and the report says why
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.ForeignId == "scene-2"), file.Path, It.IsAny<Action<MovieFile>>()), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.ForeignId == "scene-3"), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()), Times.Never());
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", file.Path, "Multi-scene file linked to Scene 2; also v2 scenes: Scene 1, Scene 3; skipped without a TPDB scene link: Scene 1", Times.Once());
        }

        [Test]
        public void rerun_mid_series_reuses_studio_and_scenes_and_stays_quiet_for_linked_files()
        {
            var series = GivenSeries(1, 11, "site-uuid", monitored: false, monitorNewItems: 1);
            var doneFile = GivenFile(1, series);
            var linkedA = GivenFile(2, series);
            var linkedB = GivenFile(3, series);
            GivenEpisode(1, series, 101, "scene-1", fileId: 1);
            GivenEpisode(2, series, 102, "scene-2", fileId: 2);
            GivenEpisode(3, series, 103, "scene-3", fileId: 3);

            // What the killed run left behind: the studio, every scene, and the first file done
            _studios["site-uuid"] = new Studio { ForeignId = "site-uuid", Monitored = false };
            _movies["scene-1"] = new Movie { Id = 1, ForeignId = "scene-1" };
            _movies["scene-2"] = new Movie { Id = 2, ForeignId = "scene-2" };
            _movies["scene-3"] = new Movie { Id = 3, ForeignId = "scene-3" };
            doneFile.Done = true;

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()))
                  .Returns(LinkOutcome.AlreadyLinked);

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), doneFile.Path, It.IsAny<Action<MovieFile>>()), Times.Never());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), linkedA.Path, It.IsAny<Action<MovieFile>>()), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), linkedB.Path, It.IsAny<Action<MovieFile>>()), Times.Once());
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());

            series.Done.Should().BeTrue();
            _files.Should().OnlyContain(f => f.Done);
            _episodes.Should().OnlyContain(e => e.Done);

            var studio = _studios["site-uuid"];

            studio.Monitored.Should().BeFalse();
            studio.WhisparrMonitorNewItems.Should().BeFalse();
            studio.SearchOnAdd.Should().BeFalse();
            Mocker.GetMock<IStudioService>().Verify(s => s.Update(studio), Times.Once());
        }

        [Test]
        public void file_left_undone_by_a_finished_series_is_parked()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var stray = GivenFile(5, series);

            series.Done = true;

            var pass = Run();

            pass.Pending.Should().Be(0);
            stray.Done.Should().BeTrue();
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(stray.Path, 4, "Files", "Series finished without this file"), Times.Once());
            Mocker.GetMock<ITpdbIdResolver>().Verify(r => r.Resolve(It.IsAny<TpdbIdKind>(), It.IsAny<IReadOnlyCollection<int>>()), Times.Never());
        }

        [Test]
        public void site_resolve_pending_waits_without_spending_an_attempt()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var file = GivenFile(5, series);
            GivenEpisode(1, series, 101, "scene-1", fileId: 5);
            _sitePending = true;

            var pass = Run();

            pass.Pending.Should().Be(2);
            pass.Waiting.Should().Be(1);
            pass.WaitingRetry.Should().Be(0);
            series.Attempts.Should().Be(0);
            series.LastAttemptAt.Should().BeNull();
            series.Done.Should().BeFalse();
            file.Done.Should().BeFalse();
            Mocker.GetMock<ILegacyV2SeriesRepository>().Verify(r => r.Update(It.IsAny<LegacyV2Series>()), Times.Never());
            Mocker.GetMock<ILegacyMigrationService>().Verify(m => m.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void scene_resolve_pending_waits_without_spending_an_attempt()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            GivenEpisode(1, series, 101, "scene-1");
            _scenePending = true;

            var pass = Run();

            pass.Pending.Should().Be(1);
            pass.Waiting.Should().Be(1);
            pass.WaitingRetry.Should().Be(0);
            series.Attempts.Should().Be(0);
            series.LastAttemptAt.Should().BeNull();
            series.Done.Should().BeFalse();
            Mocker.GetMock<ILegacyMigrationService>().Verify(m => m.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void resolve_pending_never_gives_up()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            _sitePending = true;

            for (var i = 0; i < LegacyRetryPolicy.MaxAttempts + 1; i++)
            {
                var pass = Run();

                pass.Waiting.Should().Be(1);
                pass.Pending.Should().Be(1);
            }

            series.Attempts.Should().Be(0);
            series.Done.Should().BeFalse();
            Mocker.GetMock<ILegacyMigrationService>().Verify(m => m.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void transient_exception_from_the_catalog_records_attempt_and_leaves_series_pending()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var file = GivenFile(5, series);

            Mocker.GetMock<ISceneCatalogImporter>()
                  .Setup(i => i.ImportScenes(It.IsAny<string>(), It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()))
                  .Throws(new TimeoutException("slow"));

            var pass = Run();

            pass.Pending.Should().Be(2);
            pass.WaitingRetry.Should().Be(1);
            pass.Waiting.Should().Be(0);
            pass.NextAttempt.Should().Be(1);
            series.Attempts.Should().Be(1);
            series.LastAttemptAt.Should().NotBeNull();
            series.Done.Should().BeFalse();
            file.Done.Should().BeFalse();
            Mocker.GetMock<ILegacyV2SeriesRepository>().Verify(r => r.Update(series), Times.Once());
        }

        [Test]
        public void transient_exception_from_the_catalog_is_retried()
        {
            var series = GivenSeries(1, 11, "site-uuid");

            Mocker.GetMock<ISceneCatalogImporter>()
                  .Setup(i => i.ImportScenes(It.IsAny<string>(), It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()))
                  .Throws(new TimeoutException("slow"));

            var pass = Run();

            pass.WaitingRetry.Should().Be(1);
            series.Attempts.Should().Be(1);
            series.Done.Should().BeFalse();
        }

        [Test]
        public void giving_up_reports_parks_files_and_marks_done()
        {
            var series = GivenSeries(1, 11, "site-uuid", title: "Slow Studio");
            var file = GivenFile(5, series);
            series.Attempts = LegacyRetryPolicy.MaxAttempts - 1;

            Mocker.GetMock<ISceneCatalogImporter>()
                  .Setup(i => i.ImportScenes(It.IsAny<string>(), It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()))
                  .Throws(new TimeoutException("TPDB is still resolving ids for Slow Studio"));

            var pass = Run();

            pass.Pending.Should().Be(0);
            pass.WaitingRetry.Should().Be(0);
            series.Done.Should().BeTrue();
            file.Done.Should().BeTrue();
            VerifyReport(LegacyReportCategory.NeedsAttention, "Studios", "Slow Studio", "TPDB is still resolving ids for Slow Studio", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(file.Path, 4, "Files", It.Is<string>(r => r.StartsWith("Import failed"))), Times.Once());
        }

        [Test]
        public void non_transient_series_failure_is_reported_parked_and_does_not_abort_the_pass()
        {
            var bad = GivenSeries(1, 11, "bad-site", title: "Bad Studio");
            var badFile = GivenFile(5, bad);
            var good = GivenSeries(2, 12, "good-site");

            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.FindByForeignId("bad-site"))
                  .Throws(new InvalidOperationException("boom"));

            var pass = Run();

            pass.Pending.Should().Be(0);
            pass.WaitingRetry.Should().Be(0);
            bad.Done.Should().BeTrue();
            badFile.Done.Should().BeTrue();
            good.Done.Should().BeTrue();
            bad.Attempts.Should().Be(0);
            VerifyReport(LegacyReportCategory.NeedsAttention, "Studios", "Bad Studio", "boom", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(badFile.Path, 4, "Files", "Import failed: boom"), Times.Once());
        }

        [Test]
        public void non_transient_file_failure_is_reported_and_does_not_abort_the_pass()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var bad = GivenFile(1, series);
            var good = GivenFile(2, series);
            GivenEpisode(1, series, 101, "s1", fileId: 1);
            GivenEpisode(2, series, 102, "s2", fileId: 2);

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), bad.Path, It.IsAny<Action<MovieFile>>()))
                  .Throws(new InvalidOperationException("bad row"));

            var pass = Run();

            pass.Pending.Should().Be(0);
            bad.Done.Should().BeTrue();
            good.Done.Should().BeTrue();
            series.Done.Should().BeTrue();
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", bad.Path, "bad row", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), good.Path, It.IsAny<Action<MovieFile>>()), Times.Once());
        }

        [Test]
        public void rerun_after_kill_skips_done_series_and_creates_no_duplicates()
        {
            var first = GivenSeries(1, 11, "site-1");
            var second = GivenSeries(2, 12, "site-2");
            var firstFile = GivenFile(1, first);
            var secondFile = GivenFile(2, second);
            GivenEpisode(1, first, 101, "scene-1", fileId: 1);
            GivenEpisode(2, second, 102, "scene-2", fileId: 2);

            // The first run dies on the second series
            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.FindByForeignId("site-2"))
                  .Throws(new TimeoutException("killed"));

            Run();

            first.Done.Should().BeTrue();
            second.Done.Should().BeFalse();

            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.FindByForeignId("site-2"))
                  .Returns<string>(id => _studios.GetValueOrDefault(id));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Exactly(2));
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Exactly(2));
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), firstFile.Path, It.IsAny<Action<MovieFile>>()), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), secondFile.Path, It.IsAny<Action<MovieFile>>()), Times.Once());
        }

        [Test]
        public void orphan_file_without_series_parked()
        {
            var series = GivenSeries(1, 11, "site-uuid");
            var orphan = new LegacyV2EpisodeFile { Id = 3999, LegacyId = 99, LegacySeriesId = 77, Path = "/data/Gone/x.mp4" };

            _files.Add(orphan);

            var pass = Run();

            pass.Pending.Should().Be(0);
            orphan.Done.Should().BeTrue();
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(orphan.Path, series.QualityProfileId, "Files", "No series in v2"), Times.Once());
            _progress.Verify(p => p.Total(2), Times.Once());
        }
    }
}
