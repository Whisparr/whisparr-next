using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class Add_legacy_v2_stagingFixture : MigrationTest<add_legacy_v2_staging>
    {
        [Test]
        public void should_create_staging_and_report_tables()
        {
            WithMigrationTestDb();

            // WithMigrationTestDb hands back the raw mapper, and the models go in and out through the repository
            var db = new TestDatabase(Mocker.Resolve<IDatabase>());

            db.Insert(new LegacyV2Series
            {
                LegacyId = 7,
                TpdbSiteId = 70,
                Title = "Studio",
                Path = "/media/studio",
                RootFolderPath = "/media",
                Monitored = true,
                MonitorNewItems = 1,
                QualityProfileId = 2,
                Tags = "[1]",
                SeriesType = 0,
                Added = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            });

            db.Insert(new LegacyV2Episode
            {
                LegacyId = 8,
                LegacySeriesId = 7,
                TpdbSceneId = 80,
                ExternalId = "ext",
                Title = "Scene",
                AirDate = "2024-01-02",
                Monitored = true,
                LegacyEpisodeFileId = 9
            });

            db.Insert(new LegacyV2EpisodeFile
            {
                LegacyId = 9,
                LegacySeriesId = 7,
                RelativePath = "scene.mp4",
                Path = "/media/studio/scene.mp4",
                Size = 5_000_000_000,
                Quality = "{}",
                Languages = "[1]",
                IndexerFlags = 8,
                DateAdded = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            });

            db.Insert(new LegacyMigrationReportItem
            {
                Category = LegacyReportCategory.UnmatchedFile,
                Area = "Library",
                Subject = "scene.mp4",
                Detail = "no match",
                Count = 3
            });

            var series = db.Single<LegacyV2Series>();
            series.LegacyId.Should().Be(7);
            series.TpdbSiteId.Should().Be(70);
            series.RootFolderPath.Should().Be("/media");
            series.Monitored.Should().BeTrue();
            series.Done.Should().BeFalse();

            var episode = db.Single<LegacyV2Episode>();
            episode.TpdbSceneId.Should().Be(80);
            episode.AirDate.Should().Be("2024-01-02");
            episode.LegacyEpisodeFileId.Should().Be(9);
            episode.Done.Should().BeFalse();

            var file = db.Single<LegacyV2EpisodeFile>();
            file.Size.Should().Be(5_000_000_000);
            file.IndexerFlags.Should().Be(8);
            file.MediaInfo.Should().BeNull();
            file.Done.Should().BeFalse();

            var item = db.Single<LegacyMigrationReportItem>();
            item.Category.Should().Be(LegacyReportCategory.UnmatchedFile);
            item.Count.Should().Be(3);
        }
    }
}
