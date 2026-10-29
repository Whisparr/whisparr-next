using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
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

        [Test]
        public void migration_305_should_store_the_hash_timestamps_as_timestamptz_on_postgres()
        {
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();

            if (connection is not NpgsqlConnection)
            {
                Assert.Ignore("PostgreSQL only: SQLite stores both timestamp types as DATETIME");
            }

            var types = connection.Query<string>(
                "SELECT data_type FROM information_schema.columns WHERE table_name IN ('MovieFiles', 'ImportFiles', 'LostMovieFiles') " +
                "AND column_name IN ('PHashAttemptedAt', 'HashedFileModifiedAt', 'CandidatesCheckedAt', 'LostAt')").ToList();

            types.Should().HaveCount(6);
            types.Should().OnlyContain(t => t == "timestamp with time zone");
        }
    }
}
