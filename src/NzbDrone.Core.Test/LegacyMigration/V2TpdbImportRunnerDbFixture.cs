using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Datastore.Migration.Setup.V2;

namespace NzbDrone.Core.Test.LegacyMigration
{
    // The runner against real staging tables, filled by G-c's snapshot from the v2 fixture database
    [TestFixture]
    public class V2TpdbImportRunnerDbFixture : V2PorterTestBase
    {
        private static object SeriesRow(int id, int tvdbId, string title)
        {
            return new
            {
                Id = id,
                TvdbId = tvdbId,
                Title = title,
                CleanTitle = title.ToLowerInvariant(),
                Status = 0,
                Images = "[]",
                Path = "/data/" + title,
                Monitored = true,
                QualityProfileId = 2,
                Runtime = 0,
                UseSceneNumbering = false,
                OriginalLanguage = 1,
                MonitorNewItems = 0,
                SeriesType = 0,
                Tags = "[]",
                Added = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified)
            };
        }

        private static object EpisodeRow(int id, int seriesId, int tvdbId, int fileId)
        {
            return new
            {
                Id = id,
                SeriesId = seriesId,
                SeasonNumber = 1,
                Runtime = 0,
                TvdbId = tvdbId,
                ExternalId = "ext-" + id,
                Title = "Scene " + id,
                AirDate = "2020-01-02",
                Monitored = true,
                EpisodeFileId = fileId
            };
        }

        private static object FileRow(int id, int seriesId, string name)
        {
            return new
            {
                Id = id,
                SeriesId = seriesId,
                RelativePath = name,
                Size = 1234L,
                Quality = "{\"quality\":13,\"revision\":{\"version\":1}}",
                Languages = "[1]",
                ReleaseGroup = "GRP",
                SceneName = "scene.name",
                OriginalFilePath = "/dl/scene.name.mp4",
                MediaInfo = "{}",
                IndexerFlags = 0,
                SeasonNumber = 1,
                DateAdded = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)
            };
        }

        [Test]
        public void db_backed_pass_against_fixture_staging()
        {
            InsertV2("RootFolders", new { Id = 1, Path = "/data" });
            InsertV2("Series", SeriesRow(1, 11, "Known Studio"));
            InsertV2("Series", SeriesRow(2, 22, "Lost Studio"));
            InsertV2("Episodes", EpisodeRow(10, 1, 101, 5));
            InsertV2("Episodes", EpisodeRow(11, 1, 0, 0));
            InsertV2("Episodes", EpisodeRow(20, 2, 201, 6));
            InsertV2("EpisodeFiles", FileRow(5, 1, "one.mp4"));
            InsertV2("EpisodeFiles", FileRow(6, 2, "two.mp4"));

            new V2LibrarySnapshot().Snapshot(OpenContext());
            Commit();

            var seriesRepository = Mocker.Resolve<LegacyV2SeriesRepository>();
            var episodeRepository = Mocker.Resolve<LegacyV2EpisodeRepository>();
            var fileRepository = Mocker.Resolve<LegacyV2EpisodeFileRepository>();

            Mocker.SetConstant<ILegacyMigrationStateRepository>(Mocker.Resolve<LegacyMigrationStateRepository>());
            Mocker.SetConstant<ILegacyMigrationReportRepository>(Mocker.Resolve<LegacyMigrationReportRepository>());

            var migrationService = Mocker.Resolve<LegacyMigrationService>();

            var studios = new Dictionary<string, Studio>();
            var movies = new Dictionary<string, Movie>();

            Mocker.GetMock<ITpdbIdResolver>()
                  .Setup(r => r.Resolve(TpdbIdKind.Site, It.IsAny<IReadOnlyCollection<int>>()))
                  .Returns(new TpdbResolveResult { Ids = new Dictionary<int, string> { [11] = "known-site" } });
            Mocker.GetMock<ITpdbIdResolver>()
                  .Setup(r => r.Resolve(TpdbIdKind.Scene, It.IsAny<IReadOnlyCollection<int>>()))
                  .Returns(new TpdbResolveResult { Ids = new Dictionary<int, string> { [101] = "scene-101" } });
            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                  .Returns<string>(id => studios.GetValueOrDefault(id));
            Mocker.GetMock<IAddStudioService>()
                  .Setup(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()))
                  .Returns<Studio, bool>((studio, _) => studios[studio.ForeignId] = studio);
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                  .Returns<string>(id => movies.GetValueOrDefault(id));
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns<List<Movie>, bool, bool>((added, _, _) =>
                  {
                      added.ForEach(m => movies[m.ForeignId] = m);
                      return added;
                  });
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()))
                  .Returns(LinkOutcome.Linked);

            var runner = new V2TpdbImportRunner(
                seriesRepository,
                episodeRepository,
                fileRepository,
                Mocker.GetMock<ITpdbIdResolver>().Object,
                Mocker.GetMock<IAddStudioService>().Object,
                Mocker.GetMock<IStudioService>().Object,
                Mocker.GetMock<ISceneCatalogImporter>().Object,
                Mocker.GetMock<IProvideMovieInfo>().Object,
                Mocker.GetMock<IAddMovieService>().Object,
                Mocker.GetMock<IMovieService>().Object,
                Mocker.GetMock<ILegacyFileLinker>().Object,
                migrationService,
                NLog.LogManager.GetCurrentClassLogger());

            var state = new LegacyMigrationState { SourceKind = LegacyKind.V2, SceneSource = SceneMetadataType.Tpdb };
            var progress = new Mock<ILegacyImportProgress>();

            var pass = runner.RunPass(state, progress.Object);

            pass.Pending.Should().Be(0);
            seriesRepository.All().Should().OnlyContain(s => s.Done).And.HaveCount(2);
            episodeRepository.All().Should().OnlyContain(e => e.Done).And.HaveCount(3);
            fileRepository.All().Should().OnlyContain(f => f.Done).And.HaveCount(2);

            var report = migrationService.GetReport();

            report.Select(r => (r.Category, r.Area, r.Subject, r.Detail)).Should().OnlyHaveUniqueItems();
            report.Should().Contain(r => r.Area == "Studios" && r.Subject == "Lost Studio");

            // A second pass over finished staging does nothing
            runner.RunPass(state, progress.Object).Pending.Should().Be(0);
            Mocker.GetMock<IAddStudioService>().Verify(s => s.AddStudio(It.IsAny<Studio>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()), Times.Once());
            migrationService.GetReport().Should().HaveCount(report.Count);
        }
    }
}
