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
    public class LegacyMigrationStateRepositoryFixture : DbTest<LegacyMigrationStateRepository, LegacyMigrationState>
    {
        [Test]
        public void get_current_should_return_null_then_row()
        {
            Subject.GetCurrent().Should().BeNull();

            Subject.Insert(new LegacyMigrationState
            {
                SourceKind = LegacyKind.Eros,
                SceneSource = SceneMetadataType.Tpdb,
                MovieSource = MovieMetadataType.TMDB,
                Phase = "Copy",
                Progress = "{\"done\":1}",
                StartedAt = DateTime.UtcNow,
                BackupPath = "~/backup"
            });

            var current = Subject.GetCurrent();

            current.Should().NotBeNull();
            current.SourceKind.Should().Be(LegacyKind.Eros);
            current.SceneSource.Should().Be(SceneMetadataType.Tpdb);
            current.MovieSource.Should().Be(MovieMetadataType.TMDB);
            current.Phase.Should().Be("Copy");
            current.Progress.Should().Be("{\"done\":1}");
            current.Completed.Should().BeFalse();
            current.StartedAt.Should().NotBeNull();
            current.CompletedAt.Should().BeNull();
        }
    }
}
