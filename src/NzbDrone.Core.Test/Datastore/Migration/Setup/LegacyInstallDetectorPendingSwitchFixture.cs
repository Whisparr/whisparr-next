using System;
using System.IO;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NLog;
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
    // A finished migration whose environment still names the legacy database: nobody could sign in to see the
    // instructions, because no legacy install is listed. The users are in the v5 database the detector probed.
    [TestFixture]
    public class LegacyInstallDetectorPendingSwitchFixture : TestBase
    {
        private string _appData;
        private PostgresOptions _postgres;
        private PostgresOptions _env;
        private string _v5Name;

        [SetUp]
        public void Setup()
        {
            _postgres = PostgresDatabase.GetTestOptions();

            if (string.IsNullOrEmpty(_postgres.Host))
            {
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);

            PostgresDatabase.Create(_postgres, MigrationType.Main);
            _v5Name = _postgres.MainDb;
        }

        [TearDown]
        public void Cleanup()
        {
            if (_v5Name != null)
            {
                NpgsqlConnection.ClearAllPools();
                PostgresDatabase.Drop(_postgres, MigrationType.Main);
            }
        }

        private void GivenFinishedV5(int users)
        {
            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = _postgres.Host,
                Port = _postgres.Port,
                Username = _postgres.User,
                Password = _postgres.Password,
                Database = _v5Name,
                Enlist = false
            }.ConnectionString;

            new MigrationController(LogManager.GetCurrentClassLogger())
                .Migrate(connectionString, new MigrationContext(MigrationType.Main), DatabaseType.PostgreSQL);

            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            connection.Execute("INSERT INTO \"LegacyMigrationState\" (\"SourceKind\", \"SceneSource\", \"MovieSource\", \"Phase\", \"Progress\", \"Completed\") VALUES (1, 1, 1, 'Done', '{}', true)");

            for (var i = 0; i < users; i++)
            {
                connection.Execute(
                    "INSERT INTO \"Users\" (\"Identifier\", \"Username\", \"Password\", \"Salt\", \"Iterations\") VALUES (@id, @name, 'x', 'y', 1)",
                    new { id = Guid.NewGuid().ToString(), name = "user" + i });
            }

            var legacyName = "legacy_" + GetUID();

            File.WriteAllText(Path.Combine(_appData, "config.xml"), $"<Config><PostgresMainDb>{legacyName}</PostgresMainDb></Config>");
            File.WriteAllText(Path.Combine(_appData, "config5.xml"), $"<Config><AuthenticationMethod>Forms</AuthenticationMethod><PostgresMainDb>{_v5Name}</PostgresMainDb></Config>");

            _env = new PostgresOptions { Host = _postgres.Host, Port = _postgres.Port, User = _postgres.User, Password = _postgres.Password, MainDb = legacyName };
        }

        private DetectionResult Detect()
        {
            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(_appData);

            return new LegacyInstallDetector(folders.Object, Options.Create(_env)).Detect();
        }

        [Test]
        public void should_point_sign_in_at_the_v5_database_with_its_users()
        {
            GivenFinishedV5(1);

            var result = Detect();

            result.PendingEnvSwitch.Should().BeTrue();
            result.Installs.Should().BeEmpty();
            result.HasUsableCredentials.Should().BeTrue();
            result.UserSource.Should().NotBeNull();
            result.UserSource.Type.Should().Be(DatabaseType.PostgreSQL);
            new NpgsqlConnectionStringBuilder(result.UserSource.PostgresConnectionString).Database.Should().Be(_v5Name);
        }

        [Test]
        public void should_not_count_a_v5_database_without_users_as_credentials()
        {
            GivenFinishedV5(0);

            var result = Detect();

            result.PendingEnvSwitch.Should().BeTrue();
            result.HasUsableCredentials.Should().BeFalse();
        }
    }
}
