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

        private IDbConnection OpenRawConnection()
        {
            IDbConnection connection = ConnectionInfo.DatabaseType == DatabaseType.SQLite
                ? new SQLiteConnection(ConnectionInfo.ConnectionString)
                : new NpgsqlConnection(ConnectionInfo.ConnectionString);

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
