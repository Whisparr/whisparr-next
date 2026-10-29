using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    // Phase 1 for a v2 install with the real services. Only Skyhook and the lifecycle are mocked.
    [TestFixture]
    public class V2EndToEndFixture : TestBase
    {
        private const string Username = "v2user";
        private const string Password = "hunter2";

        private string _appData;
        private string _legacyDb;
        private string _legacyConfig;
        private Mock<IAppFolderInfo> _folders;
        private Mock<ISkyhookPreflight> _preflight;
        private Mock<ILifecycleService> _lifecycle;

        private PostgresOptions _pgOptions;
        private string _pgLegacyDb;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);

            _legacyDb = Path.Combine(_appData, "whisparr2.db");
            _legacyConfig = Path.Combine(_appData, "config.xml");

            File.WriteAllText(_legacyConfig, "<Config><Port>6969</Port><AuthenticationMethod>Forms</AuthenticationMethod></Config>");

            _folders = new Mock<IAppFolderInfo>();
            _folders.SetupGet(f => f.AppDataFolder).Returns(_appData);

            _preflight = new Mock<ISkyhookPreflight>();
            _lifecycle = new Mock<ILifecycleService>();
        }

        [TearDown]
        public void Cleanup()
        {
            if (_pgOptions == null)
            {
                return;
            }

            NpgsqlConnection.ClearAllPools();

            using var admin = OpenPg("postgres");

            foreach (var db in new[] { _pgLegacyDb, Config5Seeder.V5MainDb, Config5Seeder.V5LogDb })
            {
                admin.Execute($"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)");
            }
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);

            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private SetupWizardService Wizard(DetectionResult detection)
        {
            var copier = new PostgresTableCopier();

            return new SetupWizardService(
                new SetupModeState(detection, false),
                new PreV5BackupService(_folders.Object, copier),
                new Config5Seeder(_ => null),
                new SetupRunState(_folders.Object) { PostgresOptions = _pgOptions ?? new PostgresOptions() },
                new V5DatabaseBuilder(new MigrationController(LogManager.GetCurrentClassLogger()), copier),
                new V2Porter(new V2CopyPorter(), new V2ConfigPorter(), new V2QualityPorter(), new V2ProviderPorter(), new V2LibrarySnapshot()),
                new MixedMovieResolver(),
                _preflight.Object,
                new V5DatabaseFinisher(),
                _lifecycle.Object,
                _folders.Object,
                Options.Create(_pgOptions ?? new PostgresOptions()));
        }

        private static SetupProgress RunToEnd(SetupWizardService wizard)
        {
            wizard.Start(new SetupChoice { Kind = LegacyKind.V2, SceneSource = SceneMetadataType.Tpdb, MovieSource = MovieMetadataType.TPDB });

            var deadline = DateTime.UtcNow.AddSeconds(180);

            while (wizard.Progress.Status is SetupStatus.Running or SetupStatus.Idle && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }

            return wizard.Progress;
        }

        // Rows only a real v2 install would hold. Ids are left to the database: PostgreSQL generates them always.
        private static void SeedV2(IDbConnection v2)
        {
            var added = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var quality = "{\"quality\":7,\"revision\":{\"version\":1,\"real\":0,\"isRepack\":false}}";

            LegacyDbBuilder.InsertV2Row(v2, "Tags", new { Label = "favourite" });
            LegacyDbBuilder.InsertV2Row(v2, "Tags", new { Label = "archive" });
            LegacyDbBuilder.InsertV2Row(v2, "Tags", new { Label = "vr" });
            LegacyDbBuilder.InsertV2Row(v2, "RootFolders", new { Path = "/media/scenes" });

            for (var i = 1; i <= 2; i++)
            {
                LegacyDbBuilder.InsertV2Row(v2, "Series", new
                {
                    TvdbId = 1000 + i,
                    Title = "Studio " + i,
                    CleanTitle = "studio" + i,
                    Status = 0,
                    Images = "[]",
                    Path = "/media/scenes/Studio " + i,
                    Monitored = true,
                    QualityProfileId = 1,
                    Runtime = 0,
                    UseSceneNumbering = false,
                    OriginalLanguage = 1,
                    Tags = "[1]",
                    Added = added
                });
            }

            for (var i = 1; i <= 3; i++)
            {
                LegacyDbBuilder.InsertV2Row(v2, "Episodes", new
                {
                    Monitored = true,
                    SeriesId = i == 3 ? 2 : 1,
                    SeasonNumber = 1,
                    Title = "Scene " + i,
                    Runtime = 0,
                    TvdbId = 5000 + i,
                    ExternalId = "scene-" + i,
                    AirDate = "2024-01-0" + i
                });
            }

            for (var i = 1; i <= 2; i++)
            {
                LegacyDbBuilder.InsertV2Row(v2, "EpisodeFiles", new
                {
                    SeriesId = i,
                    Quality = quality,
                    Size = 1000L * i,
                    DateAdded = added,
                    SeasonNumber = 1,
                    RelativePath = $"scene-{i}.mp4",
                    Languages = "[1]"
                });
            }

            LegacyDbBuilder.InsertV2Row(v2, "History", new
            {
                EpisodeId = 1,
                SeriesId = 1,
                SourceTitle = "Studio.Scene.1080p",
                Date = added,
                Quality = quality,
                Data = "{}",
                EventType = 1
            });

            LegacyDbBuilder.InsertV2Row(v2, "NamingConfig", new
            {
                MultiEpisodeStyle = 0,
                RenameEpisodes = true,
                StandardEpisodeFormat = "{Site Title} - {Episode Title}",
                SeriesFolderFormat = "{Site Title}",
                ReplaceIllegalCharacters = true,
                ColonReplacementFormat = 4,
                CustomColonReplacementFormat = string.Empty
            });

            LegacyDbBuilder.InsertV2Row(v2, "DownloadClients", new
            {
                Enable = true,
                Name = "SAB",
                Implementation = "Sabnzbd",
                Settings = "{\"host\":\"localhost\",\"port\":8080,\"apiKey\":\"k\",\"tvCategory\":\"adult\",\"useSsl\":false}",
                ConfigContract = "SabnzbdSettings",
                Priority = 1,
                RemoveCompletedDownloads = true,
                RemoveFailedDownloads = true,
                Tags = "[]"
            });

            LegacyDbBuilder.InsertV2Row(v2, "Indexers", new
            {
                EnableRss = true,
                EnableAutomaticSearch = true,
                EnableInteractiveSearch = true,
                Name = "Newz",
                Implementation = "Newznab",
                Settings = "{\"baseUrl\":\"http://indexer.test\",\"apiPath\":\"/api\",\"apiKey\":\"k\",\"categories\":[6000]}",
                ConfigContract = "NewznabSettings",
                Priority = 25,
                Tags = "[]",
                DownloadClientId = 0
            });

            LegacyDbBuilder.InsertV2Row(v2, "Notifications", new
            {
                Name = "Mail",
                OnGrab = true,
                OnDownload = true,
                Settings = "{\"server\":\"smtp.test\",\"port\":25,\"requireEncryption\":true,\"from\":\"a@test\",\"to\":[\"b@test\"]}",
                Implementation = "Email",
                ConfigContract = "EmailSettings",
                OnUpgrade = true,
                Tags = "[]",
                OnRename = false,
                OnHealthIssue = false
            });

            // The formula Whisparr v2 used: PBKDF2, HMACSHA512, 256-bit output, per-user iterations.
            var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), salt, 10000, HashAlgorithmName.SHA512, 32);

            LegacyDbBuilder.InsertV2Row(v2, "Users", new
            {
                Identifier = Guid.NewGuid().ToString(),
                Username,
                Password = Convert.ToBase64String(hash),
                Salt = Convert.ToBase64String(salt),
                Iterations = 10000
            });
        }

        private static long Count(IDbConnection connection, string table)
        {
            return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table}\"");
        }

        private static string ConfigValue(IDbConnection v5, string key)
        {
            return v5.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = @key", new { key });
        }

        private static IUserService UserService(Func<IDbConnection> open)
        {
            // TableMapping runs from the factory's static constructor.
            RuntimeHelpers.RunClassConstructor(typeof(DbFactory).TypeHandle);

            var database = new MainDatabase(new Database("Main", open));

            return new UserService(new UserRepository(database, new Mock<IEventAggregator>().Object));
        }

        private static void AssertPorted(IDbConnection v5, SetupProgress progress)
        {
            LegacyDatabaseGuard.IsLegacy(v5, v5 is NpgsqlConnection ? DatabaseType.PostgreSQL : DatabaseType.SQLite).Should().BeFalse();

            // The staging tables hold the library.
            Count(v5, "LegacyV2Series").Should().Be(2);
            Count(v5, "LegacyV2Episodes").Should().Be(3);
            Count(v5, "LegacyV2EpisodeFiles").Should().Be(2);
            Count(v5, "Movies").Should().Be(0);

            v5.ExecuteScalar<string>("SELECT \"Phase\" FROM \"LegacyMigrationState\"").Should().Be("Phase2Pending");
            v5.ExecuteScalar<bool>("SELECT \"Completed\" FROM \"LegacyMigrationState\"").Should().BeFalse();
            v5.ExecuteScalar<int>("SELECT \"SourceKind\" FROM \"LegacyMigrationState\"").Should().Be((int)LegacyKind.V2);
            v5.ExecuteScalar<string>("SELECT \"BackupPath\" FROM \"LegacyMigrationState\"").Should().Be(progress.BackupPath);

            Count(v5, "Tags").Should().Be(3);
        }

        [Test]
        public void v2_sqlite_end_to_end()
        {
            LegacyDbBuilder.CreateV2SqliteFromDump(_legacyDb);

            using (var v2 = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = _legacyDb, Pooling = false }.ConnectionString))
            {
                v2.Open();
                SeedV2(v2);
            }

            var detector = new LegacyInstallDetector(_folders.Object, Options.Create(new PostgresOptions()));
            var detection = detector.Detect();

            detection.SetupModeRequired.Should().BeTrue();

            var install = detection.Installs.Should().ContainSingle().Subject;

            install.Kind.Should().Be(LegacyKind.V2);
            install.Refusal.Should().Be(LegacyRefusal.None);

            var legacyHash = Hash(_legacyDb);
            var configHash = Hash(_legacyConfig);

            var progress = RunToEnd(Wizard(detection));

            progress.Status.Should().Be(SetupStatus.Restarting, progress.ErrorMessage);
            _lifecycle.Verify(l => l.Restart(), Times.Once);
            _preflight.Verify(p => p.Check(), Times.Once);

            var v5Path = Path.Combine(_appData, "whisparr5.db");

            File.Exists(v5Path).Should().BeTrue();
            File.Exists(v5Path + ".partial").Should().BeFalse();

            var v5ConnectionString = new SQLiteConnectionStringBuilder { DataSource = v5Path, Pooling = false }.ConnectionString;

            using (var v5 = new SQLiteConnection(v5ConnectionString))
            {
                v5.Open();

                AssertPorted(v5, progress);

                // The sources are locked to the choice and renaming is disabled.
                ConfigValue(v5, "whisparrscenemetadatasource").ToLowerInvariant().Should().Be("tpdb");
                ConfigValue(v5, "whisparrmoviemetadatasource").ToLowerInvariant().Should().Be("tpdb");
                ConfigValue(v5, "metadatasourceslocked").ToLowerInvariant().Should().Be("true");
                v5.ExecuteScalar<long>("SELECT COUNT(*) FROM \"NamingConfig\" WHERE \"RenameScenes\" = 1 OR \"RenameMovies\" = 1").Should().Be(0);

                // The settings came across.
                v5.ExecuteScalar<string>("SELECT \"SceneFolderFormat\" FROM \"NamingConfig\"").Should().Be("{Studio Title}");
                v5.ExecuteScalar<string>("SELECT \"Settings\" FROM \"DownloadClients\" WHERE \"Name\" = 'SAB'").Should().Contain("movieCategory").And.NotContain("tvCategory");
                Count(v5, "Indexers").Should().Be(1);
                Count(v5, "Notifications").Should().Be(1);
                Count(v5, "RootFolders").Should().Be(1);

                v5.ExecuteScalar<long>("SELECT COUNT(*) FROM \"LegacyMigrationReportItems\" WHERE \"Category\" = @category AND \"Area\" = 'History'", new { category = (int)LegacyReportCategory.LeftBehind })
                    .Should().Be(1);
            }

            // The v2 password signs in through v5.
            var users = UserService(() =>
            {
                var connection = new SQLiteConnection(v5ConnectionString);
                connection.Open();

                return connection;
            });

            users.FindUser(Username, Password).Should().NotBeNull();
            users.FindUser(Username, "wrong").Should().BeNull();

            File.ReadAllText(Path.Combine(_appData, "config5.xml")).Should().Contain("<PostgresMainDb>whisparr5-main</PostgresMainDb>");
            File.Exists(progress.BackupPath).Should().BeTrue();

            // Legacy data was only read.
            Hash(_legacyDb).Should().Be(legacyHash);
            Hash(_legacyConfig).Should().Be(configHash);

            // The run is over, and the next start does not offer the migration again.
            File.Exists(Path.Combine(_appData, "v5-setup.json")).Should().BeFalse();
            detector.Detect().SetupModeRequired.Should().BeFalse();
        }

        // PostgreSQL

        private NpgsqlConnection OpenPg(string database)
        {
            var connection = new NpgsqlConnection(PgConnectionString(database));
            connection.Open();

            return connection;
        }

        private string PgConnectionString(string database)
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = _pgOptions.Host,
                Port = _pgOptions.Port,
                Username = _pgOptions.User,
                Password = _pgOptions.Password,
                Database = database,
                Enlist = false,
                Pooling = false
            }.ConnectionString;
        }

        [Test]
        public void v2_postgres_end_to_end()
        {
            _pgOptions = PostgresDatabase.GetTestOptions();

            if (string.IsNullOrEmpty(_pgOptions.Host))
            {
                _pgOptions = null;
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            _pgLegacyDb = "v2_e2e_" + GetUID();

            using (var admin = OpenPg("postgres"))
            {
                foreach (var db in new[] { Config5Seeder.V5MainDb, Config5Seeder.V5LogDb })
                {
                    admin.Execute($"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)");
                }

                admin.Execute($"CREATE DATABASE \"{_pgLegacyDb}\"");
            }

            LegacyDbBuilder.CreateV2PostgresFromDump(PgConnectionString(_pgLegacyDb));

            using (var v2 = OpenPg(_pgLegacyDb))
            {
                SeedV2(v2);
            }

            var install = new LegacyInstall
            {
                Kind = LegacyKind.V2,
                MaxMigration = 221,
                PreviousSceneSource = SceneMetadataType.Tpdb,
                PreviousMovieSource = MovieMetadataType.TPDB,
                Database = new LegacyDatabaseLocation
                {
                    Type = DatabaseType.PostgreSQL,
                    PostgresMainDb = _pgLegacyDb,
                    PostgresConnectionString = PgConnectionString(_pgLegacyDb)
                }
            };

            var detection = new DetectionResult { SetupModeRequired = true, LegacyConfigPath = _legacyConfig };
            detection.Installs.Add(install);

            var progress = RunToEnd(Wizard(detection));

            progress.Status.Should().Be(SetupStatus.Restarting, progress.ErrorMessage);
            _lifecycle.Verify(l => l.Restart(), Times.Once);

            using var v5 = OpenPg(Config5Seeder.V5MainDb);

            AssertPorted(v5, progress);

            // The id sequences continue after the ported rows, which keep their ids.
            var maxTag = v5.ExecuteScalar<int>("SELECT MAX(\"Id\") FROM \"Tags\"");

            v5.Execute("INSERT INTO \"Tags\" (\"Label\") VALUES ('brand-new')");
            v5.ExecuteScalar<int>("SELECT MAX(\"Id\") FROM \"Tags\"").Should().BeGreaterThan(maxTag);

            // The legacy database is untouched.
            using var legacy = OpenPg(_pgLegacyDb);

            Count(legacy, "Tags").Should().Be(3);
            File.Exists(Path.Combine(_appData, "v5-setup.json")).Should().BeFalse();
        }
    }
}
