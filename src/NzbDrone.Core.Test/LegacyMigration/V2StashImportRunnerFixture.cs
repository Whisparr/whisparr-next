using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class V2StashImportRunnerFixture : CoreTest<V2StashImportRunner>
    {
        private const string Root = "/data";
        private const string StashStudio = "stash-studio";

        private LegacyMigrationState _state;
        private List<LegacyV2Series> _series;
        private List<LegacyV2Episode> _episodes;
        private List<LegacyV2EpisodeFile> _files;
        private Dictionary<string, Studio> _studios;
        private Dictionary<string, Movie> _movies;
        private Dictionary<string, HashMatch> _hashes;
        private Mock<ILegacyImportProgress> _progress;
        private Func<string, Movie> _buildStub;
        private Action<MovieFile> _fill;
        private IdMapResult _mapped;
        private Movie _parsedScene;
        private DateTime _now;
        private int _nextId;

        [SetUp]
        public void Setup()
        {
            _state = new LegacyMigrationState { SourceKind = LegacyKind.V2, SceneSource = SceneMetadataType.StashDb };
            _series = new List<LegacyV2Series>();
            _episodes = new List<LegacyV2Episode>();
            _files = new List<LegacyV2EpisodeFile>();
            _studios = new Dictionary<string, Studio>();
            _movies = new Dictionary<string, Movie>();
            _hashes = new Dictionary<string, HashMatch>();
            _progress = new Mock<ILegacyImportProgress>();
            _mapped = new IdMapResult(StashStudio, IdMapMethod.Mapping);
            _parsedScene = null;
            _now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
            _nextId = 100;

            Mocker.GetMock<ISkyhookPacer>().SetupGet(p => p.UtcNow).Returns(() => _now);

            Mocker.GetMock<ILegacyV2SeriesRepository>().Setup(r => r.All()).Returns(() => _series);
            Mocker.GetMock<ILegacyV2EpisodeRepository>().Setup(r => r.All()).Returns(() => _episodes);
            Mocker.GetMock<ILegacyV2EpisodeFileRepository>().Setup(r => r.All()).Returns(() => _files);

            Mocker.GetMock<ITpdbIdResolver>()
                  .Setup(r => r.Resolve(TpdbIdKind.Site, It.IsAny<IReadOnlyCollection<int>>()))
                  .Returns<TpdbIdKind, IReadOnlyCollection<int>>((_, ids) => new TpdbResolveResult { Ids = ids.ToDictionary(i => i, i => "site-uuid") });

            Mocker.GetMock<ISceneSourceIdMapper>()
                  .Setup(m => m.MapStudio(SceneMetadataType.Tpdb, It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(() => _mapped);

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
                  .Setup(s => s.GetByStudioForeignId(StashStudio))
                  .Returns(() => _movies.Values.Where(m => m.MovieMetadata.Value.StudioForeignId == StashStudio).ToList());
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                  .Returns<string>(id => _movies.GetValueOrDefault(id));
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindScene(It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(() => _parsedScene);

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

            Mocker.GetMock<ILegacyHashMatcher>()
                  .Setup(m => m.Match(It.IsAny<IReadOnlyCollection<HashQuery>>()))
                  .Returns<IReadOnlyCollection<HashQuery>>(queries => queries.ToDictionary(q => q.Key, q => _hashes.GetValueOrDefault(q.Path) ?? new HashMatch(null, null, false)));

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Callback<Movie, string, Action<MovieFile>, MatchTier>((_, _, fill, _) => _fill = fill)
                  .Returns(LinkOutcome.Linked);
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Park(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);
        }

        private LegacyV2Series GivenSeries(int legacyId, string title = null, bool monitored = true)
        {
            var series = new LegacyV2Series
            {
                Id = legacyId + 1000,
                LegacyId = legacyId,
                TpdbSiteId = 10 + legacyId,
                Title = title ?? $"Studio {legacyId}",
                Path = $"{Root}/Studio {legacyId}",
                RootFolderPath = Root,
                Monitored = monitored,
                MonitorNewItems = 0,
                QualityProfileId = 4,
                Tags = "[2,3]"
            };

            _series.Add(series);

            return series;
        }

        private Movie GivenScene(string foreignId, string title, string date, string studio = StashStudio, bool monitored = false)
        {
            var movie = new Movie
            {
                Id = _nextId++,
                ForeignId = foreignId,
                Title = title,
                Monitored = monitored
            };

            movie.MovieMetadata.Value.StudioForeignId = studio;
            movie.MovieMetadata.Value.ReleaseDateUtc = date == null ? null : DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

            _movies[foreignId] = movie;

            return movie;
        }

        private void GivenEpisode(int legacyId, LegacyV2Series series, string title, string airDate, bool monitored = true, int fileId = 0)
        {
            _episodes.Add(new LegacyV2Episode
            {
                Id = legacyId + 2000,
                LegacyId = legacyId,
                LegacySeriesId = series.LegacyId,
                Title = title,
                AirDate = airDate,
                Monitored = monitored,
                LegacyEpisodeFileId = fileId
            });
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
                DateAdded = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            };

            _files.Add(file);

            return file;
        }

        private LegacyImportPass Run(bool recordOutages = false)
        {
            return Subject.RunPass(_state, _progress.Object, recordOutages);
        }

        private void VerifyReport(LegacyReportCategory category, string area, string subject, string detail, Times times, int count = 1)
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(category, area, subject, detail, count), times);
        }

        private void VerifyLinked(string foreignId, LegacyV2EpisodeFile file, MatchTier tier = MatchTier.Confirmed)
        {
            Mocker.GetMock<ILegacyFileLinker>()
                  .Verify(l => l.Link(It.Is<Movie>(m => m.ForeignId == foreignId), file.Path, It.IsAny<Action<MovieFile>>(), tier), Times.Once());
        }

        [Test]
        public void handles_only_v2_stashdb()
        {
            Subject.Handles(_state).Should().BeTrue();

            _state.SceneSource = SceneMetadataType.Tpdb;
            Subject.Handles(_state).Should().BeFalse();

            _state.SceneSource = SceneMetadataType.StashDb;
            _state.SourceKind = LegacyKind.Eros;
            Subject.Handles(_state).Should().BeFalse();
        }

        [Test]
        public void studio_unmapped_parks_files_and_reports()
        {
            var series = GivenSeries(1, "Lost Studio");
            var file = GivenFile(5, series);

            _mapped = new IdMapResult(null, IdMapMethod.None);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyReport(LegacyReportCategory.NeedsAttention, "Studios", "Lost Studio", "Studio not found on StashDB", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(file.Path, 4, "Files", "Studio not found on StashDB"), Times.Once());
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
            series.Done.Should().BeTrue();
            file.Done.Should().BeTrue();
        }

        [Test]
        public void site_id_zero_still_asks_the_mapper_for_a_title_match()
        {
            var series = GivenSeries(1, "Titled Studio");

            series.TpdbSiteId = 0;
            _mapped = new IdMapResult(StashStudio, IdMapMethod.Title);

            Run();

            Mocker.GetMock<ISceneSourceIdMapper>().Verify(m => m.MapStudio(SceneMetadataType.Tpdb, null, "Titled Studio"), Times.Once());
            Mocker.GetMock<ITpdbIdResolver>().Verify(r => r.Resolve(It.IsAny<TpdbIdKind>(), It.IsAny<IReadOnlyCollection<int>>()), Times.Never());
            series.Done.Should().BeTrue();
        }

        [Test]
        public void title_mapped_studio_adds_attention_row()
        {
            var series = GivenSeries(1, "Titled Studio");

            _mapped = new IdMapResult(StashStudio, IdMapMethod.Title);

            Run();

            series.Done.Should().BeTrue();
            VerifyReport(LegacyReportCategory.NeedsAttention, "Studios", "Titled Studio", "Matched by title", Times.Once());
        }

        [Test]
        public void id_mapped_studio_adds_no_attention_row()
        {
            GivenSeries(1, "Mapped Studio");

            Run();

            VerifyReport(LegacyReportCategory.NeedsAttention, "Studios", "Mapped Studio", "Matched by title", Times.Never());
        }

        [Test]
        public void catalog_added_unmonitored()
        {
            var series = GivenSeries(1, monitored: false);

            Run();

            var studio = _studios[StashStudio];

            studio.Title.Should().Be("Studio 1");
            studio.RootFolderPath.Should().Be(Root);
            studio.QualityProfileId.Should().Be(4);
            studio.Tags.Should().BeEquivalentTo(new[] { 2, 3 });
            studio.Monitored.Should().BeFalse();
            studio.SearchOnAdd.Should().BeFalse();

            var stub = _buildStub("scene-x");

            stub.ForeignId.Should().Be("scene-x");
            stub.Monitored.Should().BeFalse();
            stub.Path.Should().Be($"{Root}/Studio 1");
            stub.RootFolderPath.Should().Be(Root);
            stub.QualityProfileId.Should().Be(4);
            stub.AddOptions.SearchForMovie.Should().BeFalse();
            stub.AddOptions.AddMethod.Should().Be(AddMovieMethod.Studio);

            Mocker.GetMock<ISceneCatalogImporter>()
                  .Verify(i => i.ImportScenes(series.Title, It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()), Times.Once());
            Mocker.GetMock<IStudioService>().Verify(s => s.Update(studio), Times.Once());
            series.Done.Should().BeTrue();
        }

        [Test]
        public void existing_studio_is_reused()
        {
            GivenSeries(1);
            _studios[StashStudio] = new Studio { ForeignId = StashStudio };

            Run();

            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void file_linked_by_title_and_date_copies_monitored()
        {
            var series = GivenSeries(1);
            var scene = GivenScene("s1", "Scene One", "2024-05-06");
            var other = GivenScene("s2", "Scene Two", "2024-05-07");
            var file = GivenFile(5, series);
            var quiet = GivenFile(6, series);

            GivenEpisode(1, series, "scene one", "2024-05-06", fileId: 5);
            GivenEpisode(2, series, "Scene Two", "2024-05-07", monitored: false, fileId: 6);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyLinked("s1", file);
            VerifyLinked("s2", quiet);
            scene.Monitored.Should().BeTrue();
            other.Monitored.Should().BeFalse();
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(scene), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(other), Times.Never());
            file.Done.Should().BeTrue();
            quiet.Done.Should().BeTrue();
            _progress.Verify(p => p.Advance("Files", 1), Times.Exactly(2));
            VerifyReport(LegacyReportCategory.LeftBehind, "Scenes", series.Title, "StashDB scenes with no v2 twin, left unmonitored", Times.Never());
        }

        [Test]
        public void link_copies_the_v2_file_metadata()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series);

            GivenScene("s1", "Scene One", "2024-05-06");
            GivenEpisode(1, series, "Scene One", "2024-05-06", fileId: 5);

            Run();

            var movieFile = new MovieFile();

            _fill(movieFile);

            movieFile.Size.Should().Be(5555);
            movieFile.Quality.Quality.Should().Be(NzbDrone.Core.Qualities.Quality.HDTV1080p);
            movieFile.Languages.Select(l => l.Id).Should().Equal(1, 2);
            movieFile.ReleaseGroup.Should().Be("GRP");
            movieFile.DateAdded.Should().Be(file.DateAdded);
        }

        [Test]
        public void file_linked_by_parser_when_title_misses()
        {
            var series = GivenSeries(1);
            var scene = GivenScene("s1", "Scene One", "2024-05-06");
            var file = GivenFile(5, series, "Studio.1.24.05.06.Scene.One.mp4");

            GivenEpisode(1, series, "Another Title", "2024-05-06", fileId: 5);
            _parsedScene = scene;

            Run();

            VerifyLinked("s1", file, MatchTier.Name);
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", file.Path, "Matched by file name only; check it", Times.Once());
            Mocker.GetMock<ILegacyHashMatcher>().Verify(m => m.Match(It.IsAny<IReadOnlyCollection<HashQuery>>()), Times.Never());
        }

        [Test]
        public void parser_hit_outside_the_studio_is_ignored_and_the_file_parked()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series, "Studio.1.24.05.06.Scene.One.mp4");

            GivenEpisode(1, series, "Another Title", "2024-05-06", fileId: 5);
            _parsedScene = GivenScene("elsewhere", "Scene One", "2024-05-06", studio: "other-studio");

            Run();

            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(file.Path, 4, "Files", "Not identified on StashDB"), Times.Once());
        }

        [Test]
        public void file_without_an_episode_goes_to_the_parser_and_hash()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series);

            GivenScene("s1", "Scene One", "2024-05-06");
            _hashes[file.Path] = new HashMatch("s1", "abc", false);

            Run();

            VerifyLinked("s1", file, MatchTier.Hash);
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", file.Path, "Matched by file name only; check it", Times.Never());
        }

        [Test]
        public void file_linked_by_oshash_adds_scene_outside_studio()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series);
            var unhashable = GivenFile(6, series);

            GivenEpisode(1, series, "No Match", "2024-05-06", fileId: 5);
            GivenScene("s1", "Scene One", "2024-05-06");
            _hashes[file.Path] = new HashMatch("outside", "abc", false);

            Run();

            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovies(It.Is<List<Movie>>(l => l.Count == 1 && l[0].ForeignId == "outside" && l[0].Path == $"{Root}/Studio 1"), true, true), Times.Once());
            VerifyLinked("outside", file, MatchTier.Hash);

            // Added unmonitored like every stub, then monitored by the v2 episode that owned the file
            _movies["outside"].Monitored.Should().BeTrue();
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(unhashable.Path, 4, "Files", "Not identified on StashDB"), Times.Once());
            Mocker.GetMock<ILegacyHashMatcher>()
                  .Verify(m => m.Match(It.Is<IReadOnlyCollection<HashQuery>>(q => q.Count == 2 && q.All(h => h.KnownOsHash == null))), Times.Once());
        }

        [Test]
        public void oshash_hit_already_in_the_library_is_not_added_again()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series);

            GivenScene("elsewhere", "Scene One", "2024-05-06", studio: "other-studio");
            GivenEpisode(1, series, "No Match", "2024-05-06", fileId: 5);
            _hashes[file.Path] = new HashMatch("elsewhere", "abc", false);

            Run();

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            VerifyLinked("elsewhere", file, MatchTier.Hash);
        }

        [Test]
        public void ambiguous_file_parked()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series);

            GivenScene("s1", "Same Title", "2024-05-06");
            GivenScene("s2", "Same Title", "2024-05-06");
            GivenEpisode(1, series, "Same Title", "2024-05-06", fileId: 5);

            Run();

            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(file.Path, 4, "Files", "Not identified on StashDB"), Times.Once());
            file.Done.Should().BeTrue();
        }

        [Test]
        public void fingerprint_try_later_retries_series_without_spending_attempt_when_outage()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series);

            GivenScene("s1", "Scene One", "2024-05-06");
            GivenEpisode(1, series, "No Match", "2024-05-06", fileId: 5);
            _hashes[file.Path] = new HashMatch(null, "abc", true);

            var pass = Run();

            pass.Outage.Should().BeTrue();
            pass.Attempted.Should().Be(1);
            pass.Pending.Should().Be(2);
            pass.WaitingRetry.Should().Be(1);
            pass.NextDue.Should().BeNull();
            series.Attempts.Should().Be(0);
            series.LastAttemptAt.Should().BeNull();
            series.Done.Should().BeFalse();
            file.Done.Should().BeFalse();
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ILegacyMigrationService>().Verify(m => m.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<ILegacyV2SeriesRepository>().Verify(r => r.Update(It.IsAny<LegacyV2Series>()), Times.Never());
        }

        [Test]
        public void fingerprint_try_later_spends_an_attempt_when_the_pass_records_outages()
        {
            var series = GivenSeries(1);
            var file = GivenFile(5, series);

            GivenEpisode(1, series, "No Match", "2024-05-06", fileId: 5);
            _hashes[file.Path] = new HashMatch(null, "abc", true);

            var pass = Run(recordOutages: true);

            pass.WaitingRetry.Should().Be(1);
            pass.NextDue.Should().Be(_now.AddMinutes(5));
            series.Attempts.Should().Be(1);
            series.Done.Should().BeFalse();
        }

        [Test]
        public void episode_without_file_monitors_its_twin()
        {
            var series = GivenSeries(1);
            var wanted = GivenScene("s1", "Scene One", "2024-05-06");
            var unwanted = GivenScene("s2", "Scene Two", "2024-05-07");
            var untouched = GivenScene("s3", "Scene Three", "2024-05-08", monitored: false);

            GivenEpisode(1, series, "Scene One", "2024-05-06");
            GivenEpisode(2, series, "Scene Two", "2024-05-07", monitored: false);
            GivenEpisode(3, series, "No Twin", "not a date");

            Run();

            wanted.Monitored.Should().BeTrue();
            unwanted.Monitored.Should().BeFalse();
            untouched.Monitored.Should().BeFalse();
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(wanted), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Once());
            _episodes.Should().OnlyContain(e => e.Done);
        }

        [Test]
        public void unmatched_catalog_count_reported()
        {
            var series = GivenSeries(1);

            GivenScene("s1", "Scene One", "2024-05-06");
            GivenScene("s2", "Scene Two", "2024-05-07");
            GivenScene("s3", "Scene Three", "2024-05-08");
            GivenEpisode(1, series, "Scene One", "2024-05-06");

            Run();

            VerifyReport(LegacyReportCategory.LeftBehind, "Scenes", series.Title, "StashDB scenes with no v2 twin, left unmonitored", Times.Once(), count: 2);
        }

        [Test]
        public void link_outcomes_are_reported_and_a_second_file_for_a_scene_is_parked()
        {
            var series = GivenSeries(1);
            var elsewhere = GivenFile(1, series);
            var missing = GivenFile(2, series);
            var other = GivenFile(3, series);
            var directory = GivenFile(4, series);
            var outside = GivenFile(5, series);

            for (var i = 1; i <= 5; i++)
            {
                GivenScene($"s{i}", $"Scene {i}", $"2024-05-0{i}");
                GivenEpisode(i, series, $"Scene {i}", $"2024-05-0{i}", fileId: i);
            }

            var outcomes = new Dictionary<string, LinkOutcome>
            {
                [elsewhere.Path] = LinkOutcome.LinkedElsewhere,
                [missing.Path] = LinkOutcome.Missing,
                [other.Path] = LinkOutcome.SceneHasOtherFile,
                [directory.Path] = LinkOutcome.IsDirectory,
                [outside.Path] = LinkOutcome.OutsideRootFolders
            };

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns<Movie, string, Action<MovieFile>, MatchTier>((_, path, _, _) => outcomes[path]);

            Run();

            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", elsewhere.Path, "Already linked to another scene", Times.Once());
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", missing.Path, "File not found", Times.Once());
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", other.Path, $"Scene already has a file; v2 also had {other.Path}", Times.Once());
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", directory.Path, "v2 stored no file name", Times.Once());
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", outside.Path, "File is outside every root folder", Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Park(other.Path, 4, "Files", "Scene already has a file"), Times.Once());
        }

        [Test]
        public void non_transient_file_failure_is_reported_and_does_not_abort_the_pass()
        {
            var series = GivenSeries(1);
            var bad = GivenFile(1, series);
            var good = GivenFile(2, series);

            GivenScene("s1", "Scene One", "2024-05-06");
            GivenScene("s2", "Scene Two", "2024-05-07");
            GivenEpisode(1, series, "Scene One", "2024-05-06", fileId: 1);
            GivenEpisode(2, series, "Scene Two", "2024-05-07", fileId: 2);

            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), bad.Path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Throws(new InvalidOperationException("bad row"));

            var pass = Run();

            pass.Pending.Should().Be(0);
            bad.Done.Should().BeTrue();
            good.Done.Should().BeTrue();
            VerifyReport(LegacyReportCategory.NeedsAttention, "Files", bad.Path, "bad row", Times.Once());
            VerifyLinked("s2", good);
        }

        [Test]
        public void resume_skips_done_files()
        {
            var series = GivenSeries(1);
            var first = GivenFile(1, series);
            var second = GivenFile(2, series);

            var one = GivenScene("s1", "Scene One", "2024-05-06");
            var two = GivenScene("s2", "Scene Two", "2024-05-07");

            GivenScene("s3", "Scene Three", "2024-05-08");
            GivenEpisode(1, series, "Scene One", "2024-05-06", fileId: 1);
            GivenEpisode(2, series, "No Match", "2024-05-07", fileId: 2);
            _hashes[second.Path] = new HashMatch(null, "abc", true);

            Run();

            first.Done.Should().BeTrue();
            second.Done.Should().BeFalse();
            series.Done.Should().BeFalse();

            _hashes[second.Path] = new HashMatch("s2", "abc", false);

            var pass = Run();

            pass.Pending.Should().Be(0);
            series.Done.Should().BeTrue();
            VerifyLinked("s1", first);
            VerifyLinked("s2", second, MatchTier.Hash);
            one.Monitored.Should().BeTrue();
            two.Monitored.Should().BeTrue();
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(one), Times.Once());
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Once());
            VerifyReport(LegacyReportCategory.LeftBehind, "Scenes", series.Title, "StashDB scenes with no v2 twin, left unmonitored", Times.Once(), count: 1);
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once());
        }

        [Test]
        public void giving_up_still_applies_the_v2_settings_to_the_studio_that_was_created()
        {
            var series = GivenSeries(1, monitored: true);

            GivenSeries(2);
            series.Attempts = LegacyRetryPolicy.MaxAttempts - 1;

            Mocker.GetMock<ISceneCatalogImporter>()
                  .Setup(i => i.ImportScenes(series.Title, It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()))
                  .Throws(new TimeoutException("slow"));

            Run();

            series.Done.Should().BeTrue();
            _studios[StashStudio].Monitored.Should().BeTrue();
            Mocker.GetMock<IStudioService>().Verify(s => s.Update(_studios[StashStudio]), Times.AtLeastOnce());
        }
    }
}
