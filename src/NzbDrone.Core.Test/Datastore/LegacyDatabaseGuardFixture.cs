using System.Data;
using System.Data.SQLite;
using Dapper;
using FluentAssertions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    public abstract class LegacyDatabaseGuardFixtureBase : DbTest
    {
        private DatabaseConnectionInfo ConnectionInfo => MigrationType == MigrationType.Main
            ? Mocker.Resolve<IConnectionStringFactory>().MainDbConnection
            : Mocker.Resolve<IConnectionStringFactory>().LogDbConnection;

        private bool IsLegacy()
        {
            using var connection = OpenRawConnection();

            return LegacyDatabaseGuard.IsLegacy(connection, ConnectionInfo.DatabaseType);
        }

        private IDbConnection OpenRawConnection(string connectionString = null)
        {
            connectionString ??= ConnectionInfo.ConnectionString;

            IDbConnection connection = ConnectionInfo.DatabaseType == DatabaseType.SQLite
                ? new SQLiteConnection(connectionString)
                : new NpgsqlConnection(connectionString);

            connection.Open();

            return connection;
        }

        private void Execute(string sql)
        {
            using var connection = OpenRawConnection();

            connection.Execute(sql);
        }

        private long V5MigrationRows()
        {
            using var connection = OpenRawConnection();

            return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"VersionInfo\" WHERE \"Version\" >= 300");
        }

        [Test]
        public void should_not_flag_a_v5_database()
        {
            V5MigrationRows().Should().Be(1);
            IsLegacy().Should().BeFalse();
        }

        [Test]
        public void should_record_the_v5_marker_under_the_migration_class_name()
        {
            using var connection = OpenRawConnection();

            connection.ExecuteScalar<string>("SELECT \"Description\" FROM \"VersionInfo\" WHERE \"Version\" = 300")
                .Should().Be(nameof(NzbDrone.Core.Datastore.Migration.whisparr_v5));
        }

        [Test]
        public void should_flag_a_database_whose_migration_300_is_not_the_v5_marker()
        {
            Execute("UPDATE \"VersionInfo\" SET \"Description\" = 'some_other_migration' WHERE \"Version\" = 300");

            IsLegacy().Should().BeTrue();
        }

        [Test]
        public void should_flag_a_database_with_later_migrations_but_no_v5_marker()
        {
            Execute("UPDATE \"VersionInfo\" SET \"Version\" = 301 WHERE \"Version\" = 300");

            IsLegacy().Should().BeTrue();
        }

        [Test]
        public void should_flag_a_legacy_version_table_found_through_the_search_path()
        {
            if (ConnectionInfo.DatabaseType != DatabaseType.PostgreSQL)
            {
                Assert.Ignore("PostgreSQL only: search_path resolution");
            }

            // FluentMigrator reads VersionInfo unqualified, so a legacy table earlier in the search path
            // is what it would migrate, even when public holds none.
            Execute("DROP TABLE public.\"VersionInfo\"");
            Execute("CREATE SCHEMA legacy");
            Execute("CREATE TABLE legacy.\"VersionInfo\" (\"Version\" bigint NOT NULL, \"AppliedOn\" timestamp with time zone, \"Description\" varchar(1024))");
            Execute("INSERT INTO legacy.\"VersionInfo\" (\"Version\", \"AppliedOn\", \"Description\") VALUES (247, now(), 'lowercase_tpdb_foreign_ids')");

            var connectionString = new NpgsqlConnectionStringBuilder(ConnectionInfo.ConnectionString) { SearchPath = "legacy" }.ConnectionString;

            using var connection = OpenRawConnection(connectionString);

            LegacyDatabaseGuard.IsLegacy(connection, DatabaseType.PostgreSQL).Should().BeTrue();
        }

        [Test]
        public void should_flag_a_database_without_v5_migrations()
        {
            Execute("DELETE FROM \"VersionInfo\" WHERE \"Version\" >= 300");

            IsLegacy().Should().BeTrue();
        }

        [Test]
        public void should_not_flag_an_empty_database()
        {
            Execute("DROP TABLE \"VersionInfo\"");

            IsLegacy().Should().BeFalse();
        }

        [Test]
        public void should_refuse_to_migrate_a_legacy_database()
        {
            Execute("DELETE FROM \"VersionInfo\" WHERE \"Version\" >= 300");

            var ex = Assert.Throws<LegacyDatabaseException>(() =>
                Mocker.Resolve<MigrationController>().Migrate(ConnectionInfo.ConnectionString, new MigrationContext(MigrationType), ConnectionInfo.DatabaseType));

            ex.Message.Should().Contain(MigrationType == MigrationType.Main ? "Whisparr__Postgres__MainDb" : "Whisparr__Postgres__LogDb");
            V5MigrationRows().Should().Be(0);
        }

        [Test]
        public void should_propagate_the_legacy_error_unwrapped_from_the_factory()
        {
            Execute("DELETE FROM \"VersionInfo\" WHERE \"Version\" >= 300");

            Assert.Throws<LegacyDatabaseException>(() => Mocker.Resolve<DbFactory>().Create(MigrationType));
        }
    }

    [TestFixture]
    public class LegacyDatabaseGuardMainFixture : LegacyDatabaseGuardFixtureBase
    {
        protected override MigrationType MigrationType => MigrationType.Main;
    }

    [TestFixture]
    public class LegacyDatabaseGuardLogFixture : LegacyDatabaseGuardFixtureBase
    {
        protected override MigrationType MigrationType => MigrationType.Log;
    }
}
