using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacySceneSourceSeedRepositoryFixture : DbTest<LegacyStudioSeedRepository, LegacyStudioSeed>
    {
        private readonly TimeSpan _dateTimePrecision = TimeSpan.FromMilliseconds(20);

        [SetUp]
        public void Setup()
        {
            AssertionConfiguration.Current.Equivalency.Modify(options =>
                options.Using<DateTime>(ctx =>
                        ctx.Subject.Should().BeCloseTo(ctx.Expectation.ToUniversalTime(), _dateTimePrecision))
                    .WhenTypeIs<DateTime>());
        }

        [Test]
        public void seed_rows_round_trip()
        {
            var studios = Mocker.Resolve<LegacyStudioSeedRepository>();
            var performers = Mocker.Resolve<LegacyPerformerSeedRepository>();
            var scenes = Mocker.Resolve<LegacySceneSeedRepository>();

            var studio = studios.Insert(new LegacyStudioSeed
            {
                LegacyForeignId = "stash-studio",
                LegacyTpdbId = "tpdb-studio",
                Title = "Studio",
                Monitored = true,
                MoviesMonitored = true,
                WhisparrMonitorNewItems = true,
                AfterDate = DateTime.UtcNow.AddDays(-3),
                RootFolderPath = "/data/studios",
                QualityProfileId = 2,
                Tags = "[1,2]",
                NewForeignId = "new-studio",
                Resolved = true,
                Attempts = 3,
                LastAttemptAt = DateTime.UtcNow
            });

            var performer = performers.Insert(new LegacyPerformerSeed
            {
                LegacyForeignId = "stash-performer",
                LegacyTpdbId = "tpdb-performer",
                Name = "Performer",
                Monitored = true,
                MoviesMonitored = true,
                WhisparrMonitorNewItems = true,
                AfterDate = DateTime.UtcNow.AddDays(-4),
                RootFolderPath = "/data/performers",
                QualityProfileId = 3,
                Tags = "[3]",
                NewForeignId = "new-performer",
                Resolved = true,
                Attempts = 1,
                LastAttemptAt = DateTime.UtcNow
            });

            var scene = scenes.Insert(new LegacySceneSeed
            {
                LegacyForeignId = "stash-scene",
                Title = "Scene",
                ReleaseDate = DateTime.UtcNow.AddDays(-10),
                LegacyStudioForeignId = "stash-studio",
                Monitored = true,
                QualityProfileId = 4,
                Path = "/data/studios/Studio/Scene",
                RootFolderPath = "/data/studios",
                Tags = "[4]",
                ImportFileId = 7,
                FileSize = 5_000_000_000L,
                FileQuality = "{\"quality\":1}",
                FileLanguages = "[1]",
                FileMediaInfo = "{\"w\":1920}",
                FileReleaseGroup = "GRP",
                FileSceneName = "scene.name",
                FileOriginalFilePath = "/in/scene.mkv",
                FileIndexerFlags = 2,
                FileEdition = "Uncut",
                FileDateAdded = DateTime.UtcNow.AddDays(-1),
                Resolved = true,
                Attempts = 2,
                LastAttemptAt = DateTime.UtcNow
            });

            studios.Get(studio.Id).Should().BeEquivalentTo(studio);
            performers.Get(performer.Id).Should().BeEquivalentTo(performer);
            scenes.Get(scene.Id).Should().BeEquivalentTo(scene);

            studios.Get(studio.Id).Attempts.Should().Be(3);
            scenes.Get(scene.Id).FileSize.Should().Be(5_000_000_000L);
            scenes.Get(scene.Id).FileMediaInfo.Should().Be("{\"w\":1920}");
        }

        [Test]
        public void state_rebuild_defaults_false()
        {
            var states = Mocker.Resolve<LegacyMigrationStateRepository>();

            var state = states.Insert(new LegacyMigrationState
            {
                SourceKind = LegacyKind.Eros,
                SceneSource = SceneMetadataType.Tpdb,
                MovieSource = MovieMetadataType.TMDB,
                Phase = "Copy"
            });

            states.Get(state.Id).Rebuild.Should().BeFalse();
        }
    }
}
