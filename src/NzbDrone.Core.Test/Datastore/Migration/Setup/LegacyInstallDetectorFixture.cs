using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class LegacyInstallDetectorFixture : TestBase
    {
        private PostgresOptions _options;
        private string _appData;

        [SetUp]
        public void Setup()
        {
            _options = new PostgresOptions();
            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);
        }

        private string DbPath(string name) => Path.Combine(_appData, name);

        private DetectionResult Detect()
        {
            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(_appData);

            return new LegacyInstallDetector(folders.Object, Options.Create(_options)).Detect();
        }

        private string CreateEros(string name = "whisparr3.db")
        {
            var path = DbPath(name);
            LegacyDbBuilder.CreateErosSqlite(path);

            return path;
        }

        private string CreateV2(long maxVersion = 221)
        {
            var path = DbPath("whisparr2.db");
            LegacyDbBuilder.CreateV2Sqlite(path, maxVersion);

            return path;
        }

        private void WriteLegacyConfig(string authenticationMethod)
        {
            File.WriteAllText(DbPath("config.xml"), $"<Config><AuthenticationMethod>{authenticationMethod}</AuthenticationMethod></Config>");
        }

        [Test]
        public void should_not_require_setup_for_empty_appdata()
        {
            var result = Detect();

            result.SetupModeRequired.Should().BeFalse();
            result.Installs.Should().BeEmpty();
            result.LegacyConfigPath.Should().Be(DbPath("config.xml"));
        }

        [Test]
        public void should_detect_v2_sqlite()
        {
            var path = CreateV2();
            LegacyDbBuilder.InsertSeries(path, 1);
            LegacyDbBuilder.InsertSeries(path, 2);
            LegacyDbBuilder.InsertEpisode(path, 1, 1, 1);
            LegacyDbBuilder.InsertEpisode(path, 2, 1);
            LegacyDbBuilder.InsertEpisode(path, 3, 2);
            LegacyDbBuilder.InsertEpisodeFile(path, 1, 1);

            var result = Detect();

            result.SetupModeRequired.Should().BeTrue();

            var install = result.Installs.Should().ContainSingle().Subject;

            install.Kind.Should().Be(LegacyKind.V2);
            install.Refusal.Should().Be(LegacyRefusal.None);
            install.MaxMigration.Should().Be(221);
            install.Studios.Should().Be(2);
            install.Scenes.Should().Be(3);
            install.Movies.Should().Be(0);
            install.Files.Should().Be(1);
            install.PreviousSceneSource.Should().Be(SceneMetadataType.Tpdb);
            install.PreviousMovieSource.Should().Be(MovieMetadataType.TPDB);
            install.Database.Type.Should().Be(DatabaseType.SQLite);
            install.Database.SqlitePath.Should().Be(path);
        }

        [Test]
        public void should_detect_eros_sqlite_with_previous_movie_source()
        {
            var path = CreateEros();
            LegacyDbBuilder.InsertStudio(path, "studio-1");
            LegacyDbBuilder.InsertErosItem(path, "scene-1", 1, 1);
            LegacyDbBuilder.InsertErosItem(path, "scene-2", 1, 1);
            LegacyDbBuilder.InsertErosItem(path, "movie-1", 0, 2);
            LegacyDbBuilder.InsertErosFile(path);
            LegacyDbBuilder.InsertConfig(path, "whisparrmoviemetadatasource", "TPDB");

            var install = Detect().Installs.Should().ContainSingle().Subject;

            install.Kind.Should().Be(LegacyKind.Eros);
            install.Refusal.Should().Be(LegacyRefusal.None);
            install.MaxMigration.Should().Be(LegacyDbBuilder.ErosVersion);
            install.Studios.Should().Be(1);
            install.Scenes.Should().Be(2);
            install.Movies.Should().Be(1);
            install.Files.Should().Be(1);
            install.PreviousSceneSource.Should().Be(SceneMetadataType.StashDb);
            install.PreviousMovieSource.Should().Be(MovieMetadataType.TPDB);
            install.HasMixedMovies.Should().BeFalse();
        }

        [Test]
        public void should_default_eros_movie_source_to_tmdb_when_unset()
        {
            CreateEros();

            Detect().Installs.Single().PreviousMovieSource.Should().Be(MovieMetadataType.TMDB);
        }

        [Test]
        public void should_detect_both_installs()
        {
            CreateV2();
            CreateEros();

            var result = Detect();

            result.SetupModeRequired.Should().BeTrue();
            result.Installs.Select(i => i.Kind).Should().BeEquivalentTo(new[] { LegacyKind.V2, LegacyKind.Eros });
        }

        [Test]
        public void should_not_require_setup_when_marked_v5_db_exists()
        {
            LegacyDbBuilder.CreateV5Sqlite(DbPath("whisparr5.db"));
            CreateEros();

            var result = Detect();

            result.SetupModeRequired.Should().BeFalse();
            result.Installs.Should().BeEmpty();
        }

        [Test]
        public void should_refuse_v2_older_than_221()
        {
            CreateV2(220);

            var result = Detect();

            result.SetupModeRequired.Should().BeTrue();
            result.Installs.Single().Refusal.Should().Be(LegacyRefusal.V2TooOld);
        }

        [Test]
        public void should_refuse_eros_with_unknown_migration()
        {
            var path = CreateEros();
            LegacyDbBuilder.InsertVersion(path, 250);

            Detect().Installs.Single().Refusal.Should().Be(LegacyRefusal.ErosNewerThanV5);
        }

        [Test]
        public void should_flag_mixed_movies()
        {
            var path = CreateEros();
            LegacyDbBuilder.InsertErosItem(path, "movie-1", 0, 0);
            LegacyDbBuilder.InsertErosItem(path, "movie-2", 0, 2);

            Detect().Installs.Single().HasMixedMovies.Should().BeTrue();
        }

        [TestCase("Forms", true, true)]
        [TestCase("Forms", false, false)]
        [TestCase("None", true, false)]
        [TestCase("Oidc", false, true)]
        public void should_report_credentials_usable_only_with_forms_and_a_user(string method, bool hasUser, bool expected)
        {
            var path = CreateEros();

            if (hasUser)
            {
                LegacyDbBuilder.InsertUser(path);
            }

            WriteLegacyConfig(method);

            Detect().Installs.Single().HasUsableCredentials.Should().Be(expected);
        }

        [Test]
        public void should_not_change_legacy_file_bytes()
        {
            var path = CreateEros();
            LegacyDbBuilder.InsertErosItem(path, "scene-1", 1, 1);

            var before = Hash(path);

            Detect();

            Hash(path).Should().Be(before);
            File.Exists(path + "-journal").Should().BeFalse();
            File.Exists(path + "-wal").Should().BeFalse();
            File.Exists(path + "-shm").Should().BeFalse();
        }

        [Test]
        public void should_report_an_error_for_an_unreadable_legacy_file()
        {
            File.WriteAllText(DbPath("whisparr3.db"), "not a database");

            var result = Detect();

            result.SetupModeRequired.Should().BeTrue();
            result.Installs.Should().BeEmpty();
            result.Errors.Should().ContainSingle().Which.Should().Contain("whisparr3.db");
        }

        [Test]
        public void should_treat_unreadable_v5_db_as_marked()
        {
            File.WriteAllText(DbPath("whisparr5.db"), "not a database");
            CreateEros();

            var result = Detect();

            result.SetupModeRequired.Should().BeFalse();
            result.Installs.Should().BeEmpty();
        }

        [Test]
        public void should_report_error_for_unreachable_configured_postgres()
        {
            // Nothing listens on port 1.
            _options = new PostgresOptions { Host = "127.0.0.1", Port = 1, User = "postgres", Password = "secret-pw" };

            var result = Detect();

            result.SetupModeRequired.Should().BeTrue();
            result.Installs.Should().BeEmpty();
            result.Errors.Should().ContainSingle().Which.Should().NotContain("secret-pw");
        }

        [Test]
        public void should_not_change_a_wal_mode_legacy_file()
        {
            var legacy = CreateEros();
            LegacyDbBuilder.InsertErosItem(legacy, "scene-1", 1, 1);
            LegacyDbBuilder.EnableWal(legacy);

            var before = Hash(legacy);

            Detect().Installs.Should().ContainSingle().Which.Scenes.Should().Be(1);

            Hash(legacy).Should().Be(before);
            File.Exists(legacy + "-wal").Should().BeFalse();
            File.Exists(legacy + "-shm").Should().BeFalse();
            File.Exists(legacy + "-journal").Should().BeFalse();
        }

        [Test]
        public void should_not_change_a_wal_mode_v5_file()
        {
            var v5 = DbPath("whisparr5.db");
            LegacyDbBuilder.CreateV5Sqlite(v5);
            LegacyDbBuilder.EnableWal(v5);

            var before = Hash(v5);

            Detect().SetupModeRequired.Should().BeFalse();

            Hash(v5).Should().Be(before);
            File.Exists(v5 + "-wal").Should().BeFalse();
            File.Exists(v5 + "-shm").Should().BeFalse();
            File.Exists(v5 + "-journal").Should().BeFalse();
        }

        [Test]
        public void should_fall_back_to_tmdb_for_an_undefined_movie_source_number()
        {
            var path = CreateEros();
            LegacyDbBuilder.InsertConfig(path, "whisparrmoviemetadatasource", "7");

            Detect().Installs.Single().PreviousMovieSource.Should().Be(MovieMetadataType.TMDB);
        }

        [Test]
        public void should_ignore_a_database_that_is_neither_v2_nor_eros()
        {
            var path = DbPath("whisparr3.db");
            LegacyDbBuilder.CreateV2Sqlite(path, 221);
            LegacyDbBuilder.DropTable(path, "Series");

            Detect().SetupModeRequired.Should().BeFalse();
        }

        [Test]
        public void should_open_sqlite_read_only()
        {
            var path = CreateEros();

            using var connection = LegacyConnections.OpenSqliteReadOnly(path);

            Assert.Throws<System.Data.SQLite.SQLiteException>(() => connection.Execute("CREATE TABLE x (a INTEGER)"));
        }

        [Test]
        public void should_fail_to_open_a_missing_sqlite_file()
        {
            Assert.Throws<FileNotFoundException>(() => LegacyConnections.OpenSqliteReadOnly(DbPath("missing.db")));
            File.Exists(DbPath("missing.db")).Should().BeFalse();
        }

        [Test]
        public void should_detect_eros_postgres()
        {
            var options = RequirePostgres();
            var database = CreatePostgresEros(options);

            try
            {
                // Named by the legacy config.xml, with the server coming from the environment.
                _options = new PostgresOptions { Host = options.Host, Port = options.Port, User = options.User, Password = options.Password };
                File.WriteAllText(DbPath("config.xml"), $"<Config><PostgresMainDb>{database}</PostgresMainDb></Config>");

                var install = Detect().Installs.Should().ContainSingle().Subject;

                install.Kind.Should().Be(LegacyKind.Eros);
                install.Database.Type.Should().Be(DatabaseType.PostgreSQL);
                install.Database.PostgresMainDb.Should().Be(database);
                install.Database.NamedByEnvironment.Should().BeFalse();
                install.MaxMigration.Should().Be(LegacyDbBuilder.ErosVersion);
                install.Refusal.Should().Be(LegacyRefusal.None);
            }
            finally
            {
                PostgresDatabase.Drop(WithMainDb(options, database), MigrationType.Main);
            }
        }

        [Test]
        public void should_mark_env_named_legacy_db()
        {
            var options = RequirePostgres();
            var database = CreatePostgresEros(options);

            try
            {
                _options = WithMainDb(options, database);

                var install = Detect().Installs.Should().ContainSingle().Subject;

                install.Database.PostgresMainDb.Should().Be(database);
                install.Database.NamedByEnvironment.Should().BeTrue();
            }
            finally
            {
                PostgresDatabase.Drop(WithMainDb(options, database), MigrationType.Main);
            }
        }

        [Test]
        public void should_flag_pending_env_switch_when_completed_v5_db_and_env_names_legacy()
        {
            var options = RequirePostgres();
            var legacyName = "legacy_" + TestBase.GetUID();
            var v5Name = GivenCompletedV5Postgres(options, true);

            try
            {
                GivenPendingEnvSwitchFiles(options, legacyName, v5Name);

                var result = Detect();

                result.PendingEnvSwitch.Should().BeTrue();
                result.SetupModeRequired.Should().BeTrue();
                result.Installs.Should().BeEmpty();
                result.EnvTargets.Should().ContainSingle().Which.Should().BeEquivalentTo(new EnvTarget { Name = "Whisparr__Postgres__MainDb", Value = v5Name });
            }
            finally
            {
                PostgresDatabase.Drop(WithMainDb(options, v5Name), MigrationType.Main);
            }
        }

        [Test]
        public void should_fill_env_targets_for_a_connection_string_variable()
        {
            var options = RequirePostgres();
            var legacyName = "legacy_" + TestBase.GetUID();
            var v5Name = GivenCompletedV5Postgres(options, true);

            try
            {
                GivenPendingEnvSwitchFiles(options, legacyName, v5Name);

                _options.MainDb = null;
                _options.MainDbConnectionString = $"Host={options.Host};Port={options.Port};Username={options.User};Password={options.Password};Database={legacyName}";

                var target = Detect().EnvTargets.Should().ContainSingle().Subject;

                target.Name.Should().Be("Whisparr__Postgres__MainDbConnectionString");
                new NpgsqlConnectionStringBuilder(target.Value).Database.Should().Be(v5Name);
                new NpgsqlConnectionStringBuilder(target.Value).Host.Should().Be(options.Host);
            }
            finally
            {
                PostgresDatabase.Drop(WithMainDb(options, v5Name), MigrationType.Main);
            }
        }

        [Test]
        public void should_not_flag_pending_env_switch_without_migration_state_row()
        {
            var options = RequirePostgres();
            var legacyName = "legacy_" + TestBase.GetUID();
            var v5Name = GivenCompletedV5Postgres(options, false);

            try
            {
                GivenPendingEnvSwitchFiles(options, legacyName, v5Name);

                var result = Detect();

                result.PendingEnvSwitch.Should().BeFalse();
                result.EnvTargets.Should().BeEmpty();
            }
            finally
            {
                PostgresDatabase.Drop(WithMainDb(options, v5Name), MigrationType.Main);
            }
        }

        [Test]
        public void should_not_flag_pending_env_switch_when_env_names_the_v5_db()
        {
            var options = RequirePostgres();
            var v5Name = GivenCompletedV5Postgres(options, true);

            try
            {
                GivenPendingEnvSwitchFiles(options, "legacy_" + TestBase.GetUID(), v5Name);

                _options.MainDb = v5Name;

                var result = Detect();

                result.PendingEnvSwitch.Should().BeFalse();
                result.SetupModeRequired.Should().BeFalse();
            }
            finally
            {
                PostgresDatabase.Drop(WithMainDb(options, v5Name), MigrationType.Main);
            }
        }

        private static string GivenCompletedV5Postgres(PostgresOptions options, bool withStateRow)
        {
            PostgresDatabase.Create(options, MigrationType.Main);

            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = options.MainDb,
                Enlist = false
            }.ConnectionString;

            new MigrationController(LogManager.GetCurrentClassLogger())
                .Migrate(connectionString, new MigrationContext(MigrationType.Main), DatabaseType.PostgreSQL);

            if (withStateRow)
            {
                using var connection = new NpgsqlConnection(connectionString);
                connection.Open();
                connection.Execute("INSERT INTO \"LegacyMigrationState\" (\"SourceKind\", \"SceneSource\", \"MovieSource\", \"Phase\", \"Progress\", \"Completed\") VALUES (1, 1, 1, 'Done', '{}', true)");
            }

            return options.MainDb;
        }

        // config5.xml names the v5 database, config.xml and the environment still name the legacy one.
        private void GivenPendingEnvSwitchFiles(PostgresOptions options, string legacyName, string v5Name)
        {
            File.WriteAllText(DbPath("config.xml"), $"<Config><PostgresMainDb>{legacyName}</PostgresMainDb></Config>");
            File.WriteAllText(DbPath("config5.xml"), $"<Config><PostgresMainDb>{v5Name}</PostgresMainDb></Config>");

            _options = new PostgresOptions { Host = options.Host, Port = options.Port, User = options.User, Password = options.Password, MainDb = legacyName };
        }

        private static PostgresOptions RequirePostgres()
        {
            var options = PostgresDatabase.GetTestOptions();

            if (string.IsNullOrEmpty(options.Host))
            {
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            return options;
        }

        private static PostgresOptions WithMainDb(PostgresOptions options, string mainDb)
        {
            return new PostgresOptions
            {
                Host = options.Host,
                Port = options.Port,
                User = options.User,
                Password = options.Password,
                MainDb = mainDb
            };
        }

        private static string CreatePostgresEros(PostgresOptions options)
        {
            PostgresDatabase.Create(options, MigrationType.Main);

            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = options.MainDb,
                Enlist = false
            }.ConnectionString;

            new MigrationController(LogManager.GetCurrentClassLogger())
                .Migrate(connectionString, new MigrationContext(MigrationType.Main, LegacyDbBuilder.ErosVersion), DatabaseType.PostgreSQL);

            return options.MainDb;
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);

            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
