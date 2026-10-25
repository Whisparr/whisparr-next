using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    // Phase 1 with the real services on SQLite. Only Skyhook and the lifecycle are mocked.
    [TestFixture]
    public class ErosSameSourceEndToEndFixture : TestBase
    {
        private string _appData;
        private string _legacyDb;
        private string _legacyConfig;
        private Mock<IAppFolderInfo> _folders;
        private Mock<ISkyhookPreflight> _preflight;
        private Mock<ILifecycleService> _lifecycle;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);

            _legacyDb = Path.Combine(_appData, "whisparr3.db");
            _legacyConfig = Path.Combine(_appData, "config.xml");

            LegacyDbBuilder.CreateErosSqlite(_legacyDb);
            LegacyDbBuilder.InsertStudio(_legacyDb, "studio-1");
            LegacyDbBuilder.InsertStudio(_legacyDb, "studio-2");
            LegacyDbBuilder.InsertErosItem(_legacyDb, "scene-1", 1, 3);
            LegacyDbBuilder.InsertErosItem(_legacyDb, "scene-2", 1, 3);
            LegacyDbBuilder.InsertErosItem(_legacyDb, "movie-1", 0, 2);
            LegacyDbBuilder.InsertErosFile(_legacyDb);
            LegacyDbBuilder.InsertConfig(_legacyDb, "whisparrmoviemetadatasource", "TPDB");

            File.WriteAllText(_legacyConfig, "<Config><Port>6969</Port><AuthenticationMethod>None</AuthenticationMethod></Config>");

            _folders = new Mock<IAppFolderInfo>();
            _folders.SetupGet(f => f.AppDataFolder).Returns(_appData);

            _preflight = new Mock<ISkyhookPreflight>();
            _lifecycle = new Mock<ILifecycleService>();
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);

            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private SetupWizardService Wizard(DetectionResult detection)
        {
            var copier = new PostgresTableCopier();
            var options = Options.Create(new PostgresOptions());

            return new SetupWizardService(
                new SetupModeState(detection, false),
                new PreV5BackupService(_folders.Object, copier),
                new Config5Seeder(_ => null),
                new SetupRunState(_folders.Object) { PostgresOptions = new PostgresOptions() },
                new V5DatabaseBuilder(new MigrationController(LogManager.GetCurrentClassLogger()), copier),
                new MixedMovieResolver(),
                _preflight.Object,
                new V5DatabaseFinisher(),
                _lifecycle.Object,
                _folders.Object,
                options);
        }

        private static long Count(SQLiteConnection connection, string table)
        {
            return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table}\"");
        }

        [Test]
        public void should_migrate_an_eros_sqlite_install_keeping_both_sources()
        {
            var detector = new LegacyInstallDetector(_folders.Object, Options.Create(new PostgresOptions()));
            var detection = detector.Detect();

            detection.SetupModeRequired.Should().BeTrue();

            var install = detection.Installs.Should().ContainSingle().Subject;
            var legacyHash = Hash(_legacyDb);
            var configHash = Hash(_legacyConfig);

            var wizard = Wizard(detection);

            wizard.Start(new SetupChoice
            {
                Kind = LegacyKind.Eros,
                SceneSource = install.PreviousSceneSource,
                MovieSource = install.PreviousMovieSource
            });

            var deadline = DateTime.UtcNow.AddSeconds(120);

            while (wizard.Progress.Status is SetupStatus.Running or SetupStatus.Idle && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }

            var progress = wizard.Progress;

            progress.Status.Should().Be(SetupStatus.Restarting, progress.ErrorMessage);
            _lifecycle.Verify(l => l.Restart(), Times.Once);
            _preflight.Verify(p => p.Check(), Times.Never);

            // The v5 database, marked and holding everything the legacy one did.
            var v5Path = Path.Combine(_appData, "whisparr5.db");

            File.Exists(v5Path).Should().BeTrue();
            File.Exists(v5Path + ".partial").Should().BeFalse();

            var v5ConnectionString = new SQLiteConnectionStringBuilder { DataSource = v5Path, Pooling = false }.ConnectionString;

            using (var v5 = new SQLiteConnection(v5ConnectionString))
            using (var legacy = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = _legacyDb, Pooling = false, ReadOnly = true }.ConnectionString))
            {
                v5.Open();
                legacy.Open();

                LegacyDatabaseGuard.IsLegacy(v5, DatabaseType.SQLite).Should().BeFalse();

                foreach (var table in new[] { "Studios", "Movies", "MovieMetadata", "MovieFiles" })
                {
                    Count(v5, table).Should().Be(Count(legacy, table), table);
                }

                Count(v5, "Movies").Should().Be(3);

                // The sources are locked to the choice and renaming is disabled.
                v5.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'whisparrscenemetadatasource'").Should().Be("stashdb");
                v5.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'whisparrmoviemetadatasource'").Should().Be("tpdb");
                v5.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'metadatasourceslocked'").Should().BeOneOf("True", "true");
                v5.ExecuteScalar<long>("SELECT COUNT(*) FROM \"NamingConfig\" WHERE \"RenameScenes\" = 1 OR \"RenameMovies\" = 1").Should().Be(0);

                // The state is completed, because nothing was detached.
                var state = v5.QuerySingle("SELECT * FROM \"LegacyMigrationState\"");

                ((string)state.Phase).Should().Be("Done");
                ((long)state.Completed).Should().Be(1);
                ((long)state.SourceKind).Should().Be((long)LegacyKind.Eros);
                ((string)state.BackupPath).Should().Be(progress.BackupPath);
            }

            // config5.xml names the v5 databases.
            File.ReadAllText(Path.Combine(_appData, "config5.xml")).Should().Contain("<PostgresMainDb>whisparr5-main</PostgresMainDb>");

            File.Exists(progress.BackupPath).Should().BeTrue();

            // Legacy data was only read.
            Hash(_legacyDb).Should().Be(legacyHash);
            Hash(_legacyConfig).Should().Be(configHash);

            // The run is over, and the next start does not offer the migration again.
            File.Exists(Path.Combine(_appData, "v5-setup.json")).Should().BeFalse();
            detector.Detect().SetupModeRequired.Should().BeFalse();
        }
    }
}
