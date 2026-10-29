using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class LegacyDbBuilderV2Fixture : TestBase
    {
        private string _dir;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_dir);
        }

        [Test]
        public void sqlite_dump_should_load_at_schema_221()
        {
            var path = Path.Combine(_dir, "whisparr2.db");
            LegacyDbBuilder.CreateV2SqliteFromDump(path);

            using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
            connection.Open();

            connection.ExecuteScalar<long>("SELECT MAX(\"Version\") FROM \"VersionInfo\"").Should().Be(221);
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"QualityProfiles\"").Should().Be(7);
        }

        [Test]
        public void sqlite_dump_should_be_detected_as_v2()
        {
            LegacyDbBuilder.CreateV2SqliteFromDump(Path.Combine(_dir, "whisparr2.db"));

            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(_dir);

            var result = new LegacyInstallDetector(folders.Object, Options.Create(new PostgresOptions())).Detect();

            result.Installs.Should().ContainSingle();
            result.Installs.Single().Kind.Should().Be(LegacyKind.V2);
            result.Installs.Single().Refusal.Should().Be(LegacyRefusal.None);
        }

        [Test]
        public void postgres_dump_should_load_at_schema_221()
        {
            var options = PostgresDatabase.GetTestOptions();

            if (string.IsNullOrEmpty(options.Host))
            {
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            PostgresDatabase.Create(options, MigrationType.Main);

            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = options.MainDb,
                Enlist = false,
                Pooling = false
            }.ConnectionString;

            try
            {
                LegacyDbBuilder.CreateV2PostgresFromDump(connectionString);

                using var connection = new NpgsqlConnection(connectionString);
                connection.Open();

                connection.ExecuteScalar<long>("SELECT MAX(\"Version\") FROM \"VersionInfo\"").Should().Be(221);
                connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"QualityProfiles\"").Should().Be(7);
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();

                var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
                using var adminConnection = new NpgsqlConnection(admin);
                adminConnection.Open();
                adminConnection.Execute($"DROP DATABASE IF EXISTS \"{options.MainDb}\" WITH (FORCE)");
            }
        }
    }
}
