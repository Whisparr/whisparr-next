using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class SetupWizardServiceFixture : TestBase
    {
        private string _appData;
        private List<string> _calls;
        private Mock<IPreV5BackupService> _backup;
        private Mock<IConfig5Seeder> _seeder;
        private Mock<ISetupRunState> _runState;
        private Mock<IV5DatabaseBuilder> _builder;
        private Mock<IV2Porter> _porter;
        private V2SnapshotCounts _counts;
        private Mock<IMixedMovieResolver> _resolver;
        private Mock<ISceneSourceSeeder> _sceneSeeder;
        private SceneSeedResult _seeded;
        private Mock<ISkyhookPreflight> _preflight;
        private Mock<IV5DatabaseFinisher> _finisher;
        private Mock<ILifecycleService> _lifecycle;
        private LegacyInstall _install;
        private DetectionResult _detection;
        private V5DatabaseTargets _targets;
        private MixedMovieResult _mixed;
        private PostgresOptions _options;
        private LegacyMigrationState _finishedState;
        private Mock<IDbConnection> _legacyConnection;
        private bool _seededConfig5;

        [SetUp]
        public void Setup()
        {
            _seededConfig5 = true;
            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);
            _calls = new List<string>();
            _options = new PostgresOptions();
            _targets = new V5DatabaseTargets { MainDb = "whisparr5-main", LogDb = "whisparr5-log" };
            _mixed = new MixedMovieResult();
            _finishedState = null;

            _install = new LegacyInstall
            {
                Kind = LegacyKind.Eros,
                PreviousSceneSource = SceneMetadataType.StashDb,
                PreviousMovieSource = MovieMetadataType.TPDB,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = Path.Combine(_appData, "whisparr3.db") }
            };

            _detection = new DetectionResult { SetupModeRequired = true, LegacyConfigPath = Path.Combine(_appData, "config.xml") };
            _detection.Installs.Add(_install);

            _backup = new Mock<IPreV5BackupService>();
            _backup.Setup(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()))
                .Callback(() => _calls.Add("backup"))
                .Returns("~/backup.zip");

            _seeder = new Mock<IConfig5Seeder>();
            _seeder.Setup(s => s.Seed(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LegacyInstall>()))
                .Callback(() => _calls.Add("seed"))
                .Returns(() => _targets);

            _runState = new Mock<ISetupRunState>();
            _runState.Setup(r => r.Begin(It.IsAny<bool>())).Callback(() => _calls.Add("begin"));
            _runState.Setup(r => r.Complete()).Callback(() => _calls.Add("complete"));

            _builder = new Mock<IV5DatabaseBuilder>();
            _builder.Setup(b => b.CopyEros(It.IsAny<LegacyInstall>(), It.IsAny<V5DatabaseTargets>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                .Callback(() => _calls.Add("copy"))
                .Returns(CreatePartial);

            _builder.Setup(b => b.CreateEmpty(It.IsAny<LegacyInstall>(), It.IsAny<V5DatabaseTargets>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                .Callback(() => _calls.Add("create"))
                .Returns(CreatePartial);

            _counts = new V2SnapshotCounts();
            _legacyConnection = new Mock<IDbConnection>();
            _legacyConnection.Setup(c => c.Dispose()).Callback(() => _calls.Add("dispose"));

            _porter = new Mock<IV2Porter>();
            _porter.Setup(p => p.Open(It.IsAny<LegacyInstall>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<DatabaseType>()))
                .Callback(() => _calls.Add("open"))
                .Returns(() => new V2PortContext(_legacyConnection.Object, null, null, DatabaseType.SQLite));
            _porter.Setup(p => p.PortSettings(It.IsAny<V2PortContext>())).Callback(() => _calls.Add("port"));
            _porter.Setup(p => p.SnapshotLibrary(It.IsAny<V2PortContext>()))
                .Callback(() => _calls.Add("snapshot"))
                .Returns(() => _counts);
            _porter.Setup(p => p.WriteReport(It.IsAny<V2PortContext>())).Callback(() => _calls.Add("report"));

            _resolver = new Mock<IMixedMovieResolver>();
            _resolver.Setup(r => r.Resolve(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<MovieMetadataType>()))
                .Callback(() => _calls.Add("resolve"))
                .Returns(() => _mixed);

            _seeded = new SceneSeedResult();
            _sceneSeeder = new Mock<ISceneSourceSeeder>();
            _sceneSeeder.Setup(s => s.Seed(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
                .Callback(() => _calls.Add("seedscenes"))
                .Returns(() => _seeded);

            _preflight = new Mock<ISkyhookPreflight>();
            _preflight.Setup(p => p.Check()).Callback(() => _calls.Add("preflight"));

            _finisher = new Mock<IV5DatabaseFinisher>();
            _finisher.Setup(f => f.Finish(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<SceneMetadataType>(), It.IsAny<MovieMetadataType>(), It.IsAny<LegacyMigrationState>()))
                .Callback<IDbConnection, IDbTransaction, SceneMetadataType, MovieMetadataType, LegacyMigrationState>((_, _, _, _, state) =>
                {
                    _calls.Add("finish");
                    _finishedState = state;
                });

            _lifecycle = new Mock<ILifecycleService>();
            _lifecycle.Setup(l => l.Restart()).Callback(() => _calls.Add("restart"));
        }

        private BuiltDatabases CreatePartial()
        {
            var partial = Path.Combine(_appData, V5DatabaseBuilder.PartialFileName);
            var connectionString = new SQLiteConnectionStringBuilder { DataSource = partial, Pooling = false }.ConnectionString;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                connection.Execute("CREATE TABLE IF NOT EXISTS \"T\" (\"Id\" INTEGER)");
            }

            return new BuiltDatabases { Type = DatabaseType.SQLite, SqlitePartialPath = partial, MainConnectionString = connectionString };
        }

        private SetupWizardService Service()
        {
            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(_appData);

            return new SetupWizardService(
                new SetupModeState(_detection, _seededConfig5),
                _backup.Object,
                _seeder.Object,
                _runState.Object,
                _builder.Object,
                _porter.Object,
                _resolver.Object,
                _sceneSeeder.Object,
                _preflight.Object,
                _finisher.Object,
                _lifecycle.Object,
                folders.Object,
                Options.Create(_options));
        }

        private static SetupChoice Choice() => new()
        {
            Kind = LegacyKind.Eros,
            SceneSource = SceneMetadataType.StashDb,
            MovieSource = MovieMetadataType.TPDB
        };

        private static SetupProgress Await(SetupWizardService service)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);

            while (service.Progress.Status == SetupStatus.Running && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            return service.Progress;
        }

        private SetupProgress Run(SetupWizardService service = null)
        {
            service ??= Service();
            service.Start(Choice());

            return Await(service);
        }

        [Test]
        public void should_run_steps_in_order()
        {
            var progress = Run();

            _calls.Should().Equal("backup", "begin", "seed", "copy", "resolve", "finish", "complete", "restart");
            progress.Status.Should().Be(SetupStatus.Restarting);
            progress.BackupPath.Should().Be("~/backup.zip");
            progress.EnvVarsToChange.Should().BeEmpty();
            progress.Steps.Select(s => s.Step).Should().Equal(Enum.GetValues<SetupStep>());
            progress.Steps.Where(s => s.Step is not (SetupStep.SkyhookPreflight or SetupStep.PortSettings or SetupStep.SnapshotLibrary or SetupStep.SceneSeeds)).Should().OnlyContain(s => s.State == "done");
            progress.Steps.Single(s => s.Step == SetupStep.SceneSeeds).State.Should().Be("skipped");
            _sceneSeeder.Verify(s => s.Seed(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Never);
            _runState.Verify(r => r.Begin(true), Times.Once);
        }

        [Test]
        public void should_rename_the_partial_database_into_place()
        {
            Run();

            File.Exists(Path.Combine(_appData, "whisparr5.db")).Should().BeTrue();
            File.Exists(Path.Combine(_appData, "whisparr5.db.partial")).Should().BeFalse();
        }

        [Test]
        public void should_record_the_partial_and_each_postgres_database_for_discard()
        {
            _builder.Setup(b => b.CopyEros(It.IsAny<LegacyInstall>(), It.IsAny<V5DatabaseTargets>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                .Callback<LegacyInstall, V5DatabaseTargets, Action<string>, Action<string>>((_, _, created, reused) =>
                {
                    created("made-by-run");
                    reused("empty-and-reused");
                })
                .Returns(CreatePartial);

            Run();

            _runState.Verify(r => r.RecordSqlitePartial(Path.Combine(_appData, "whisparr5.db.partial")), Times.Once);
            _runState.Verify(r => r.RecordDatabase("made-by-run"), Times.Once);
            _runState.Verify(r => r.RecordReusedEmptyTarget("empty-and-reused"), Times.Once);
        }

        [Test]
        public void backup_failure_should_stop_before_sidecar()
        {
            _backup.Setup(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()))
                .Throws(new SetupStepException("disk full"));

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("BackupFailed");
            progress.ErrorMessage.Should().Be("disk full");
            progress.CanRetry.Should().BeFalse();
            progress.Steps.Single(s => s.Step == SetupStep.Backup).State.Should().Be("failed");
            _runState.Verify(r => r.Begin(It.IsAny<bool>()), Times.Never);
            _runState.Verify(r => r.DiscardIfPresent(), Times.Never);
            _seeder.Verify(s => s.Seed(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LegacyInstall>()), Times.Never);
        }

        [Test]
        public void failure_after_begin_should_discard()
        {
            _builder.Setup(b => b.CopyEros(It.IsAny<LegacyInstall>(), It.IsAny<V5DatabaseTargets>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                .Throws(new SetupStepException("could not build"));

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorMessage.Should().Be("could not build");
            progress.ErrorKey.Should().Be("Unexpected");
            progress.CanRetry.Should().BeFalse();
            progress.Steps.Single(s => s.Step == SetupStep.CreateDatabase).State.Should().Be("failed");
            _runState.Verify(r => r.DiscardIfPresent(), Times.Once);
            _runState.Verify(r => r.Complete(), Times.Never);
            _lifecycle.Verify(l => l.Restart(), Times.Never);
        }

        [Test]
        public void failure_creating_a_postgres_database_should_be_retryable()
        {
            _install.Database.Type = DatabaseType.PostgreSQL;
            _install.Database.PostgresMainDb = "whisparr-main";
            _builder.Setup(b => b.CopyEros(It.IsAny<LegacyInstall>(), It.IsAny<V5DatabaseTargets>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                .Throws(new SetupStepException("not allowed to create databases"));

            var progress = Run();

            progress.ErrorKey.Should().Be("PostgresCreateFailed");
            progress.CanRetry.Should().BeTrue();
            _runState.Verify(r => r.DiscardIfPresent(), Times.Once);
        }

        [Test]
        public void unexpected_exception_should_fail_and_discard()
        {
            _resolver.Setup(r => r.Resolve(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<MovieMetadataType>()))
                .Throws(new InvalidOperationException("boom"));

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("Unexpected");
            progress.ErrorMessage.Should().Contain("boom");
            progress.Steps.Single(s => s.Step == SetupStep.MixedMovies).State.Should().Be("failed");
            _runState.Verify(r => r.DiscardIfPresent(), Times.Once);
            File.Exists(Path.Combine(_appData, "whisparr5.db")).Should().BeFalse();
        }

        [Test]
        public void should_mark_completed_without_detached_movies()
        {
            Run();

            _finishedState.Phase.Should().Be("Done");
            _finishedState.Completed.Should().BeTrue();
            _finishedState.CompletedAt.Should().NotBeNull();
            _finishedState.SourceKind.Should().Be(LegacyKind.Eros);
            _finishedState.BackupPath.Should().Be("~/backup.zip");
            _finisher.Verify(f => f.Finish(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), SceneMetadataType.StashDb, MovieMetadataType.TPDB, It.IsAny<LegacyMigrationState>()), Times.Once);
        }

        [Test]
        public void should_leave_incomplete_with_detached_movies()
        {
            _install.HasMixedMovies = true;
            _mixed.Detached = 2;

            var progress = Run();

            _calls.Should().ContainInOrder("preflight", "backup", "resolve", "finish");
            _finishedState.Phase.Should().Be("Phase2Pending");
            _finishedState.Completed.Should().BeFalse();
            _finishedState.CompletedAt.Should().BeNull();
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("done");
        }

        [Test]
        public void should_leave_incomplete_when_a_movie_was_seeded_without_a_file()
        {
            _install.HasMixedMovies = true;
            _mixed.Seeded = 1;

            var progress = Run();

            _calls.Should().ContainInOrder("preflight", "backup", "resolve", "finish");
            _finishedState.Phase.Should().Be("Phase2Pending");
            _finishedState.Completed.Should().BeFalse();
            _finishedState.CompletedAt.Should().BeNull();
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("done");
        }

        [Test]
        public void should_report_movies_outside_every_root_folder()
        {
            _mixed.Skipped = 2;
            _mixed.SkippedPaths.Add("/elsewhere/a");
            _mixed.SkippedPaths.Add("/elsewhere/b");

            var progress = Run();

            progress.SkippedMovies.Should().Be(2);
            progress.SkippedMoviePaths.Should().Equal("/elsewhere/a", "/elsewhere/b");
        }

        private void UseV2Install()
        {
            _install.Kind = LegacyKind.V2;
            _install.PreviousSceneSource = SceneMetadataType.Tpdb;
            _install.PreviousMovieSource = MovieMetadataType.TPDB;
        }

        private static SetupChoice V2Choice(SceneMetadataType scene = SceneMetadataType.Tpdb, MovieMetadataType movie = MovieMetadataType.TPDB) => new()
        {
            Kind = LegacyKind.V2,
            SceneSource = scene,
            MovieSource = movie
        };

        private SetupProgress RunV2()
        {
            UseV2Install();

            var service = Service();
            service.Start(V2Choice());

            return Await(service);
        }

        [Test]
        public void v2_should_run_port_and_snapshot_steps_in_order()
        {
            _counts.Series = 2;
            _install.Studios = 1;

            var progress = RunV2();

            progress.Status.Should().Be(SetupStatus.Restarting, progress.ErrorMessage);
            _calls.Should().Equal("preflight", "backup", "begin", "seed", "create", "open", "port", "snapshot", "report", "dispose", "finish", "complete", "restart");

            var steps = progress.Steps.Select(s => s.Step).ToList();

            steps.IndexOf(SetupStep.PortSettings).Should().Be(steps.IndexOf(SetupStep.Migrate) + 1);
            steps.IndexOf(SetupStep.SnapshotLibrary).Should().Be(steps.IndexOf(SetupStep.PortSettings) + 1);

            progress.Steps.Single(s => s.Step == SetupStep.Migrate).State.Should().Be("done");
            progress.Steps.Single(s => s.Step == SetupStep.PortSettings).State.Should().Be("done");
            progress.Steps.Single(s => s.Step == SetupStep.SnapshotLibrary).State.Should().Be("done");
            progress.Steps.Single(s => s.Step == SetupStep.MixedMovies).State.Should().Be("skipped");

            _builder.Verify(b => b.CopyEros(It.IsAny<LegacyInstall>(), It.IsAny<V5DatabaseTargets>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()), Times.Never);
            _resolver.Verify(r => r.Resolve(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<MovieMetadataType>()), Times.Never);
        }

        [Test]
        public void v2_should_no_longer_be_refused()
        {
            var progress = RunV2();

            progress.ErrorKey.Should().BeNull();
            progress.Status.Should().Be(SetupStatus.Restarting);
        }

        [Test]
        public void v2_should_accept_any_movie_source_with_tpdb_scenes()
        {
            UseV2Install();

            var service = Service();
            service.Start(V2Choice(SceneMetadataType.Tpdb, MovieMetadataType.TMDB));

            var progress = Await(service);

            progress.ErrorKey.Should().BeNull();
            progress.Status.Should().Be(SetupStatus.Restarting);
            _finisher.Verify(f => f.Finish(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), SceneMetadataType.Tpdb, MovieMetadataType.TMDB, It.IsAny<LegacyMigrationState>()), Times.Once);
        }

        [Test]
        public void v2_with_series_should_be_phase2_pending()
        {
            _counts.Series = 3;
            _install.Studios = 1;

            var progress = RunV2();

            _calls.Should().ContainInOrder("preflight", "backup", "report", "finish");
            _finishedState.Phase.Should().Be("Phase2Pending");
            _finishedState.Completed.Should().BeFalse();
            _finishedState.CompletedAt.Should().BeNull();
            _finishedState.SourceKind.Should().Be(LegacyKind.V2);
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("done");
        }

        [Test]
        public void v2_with_stashdb_runs()
        {
            UseV2Install();
            _counts.Series = 2;
            _install.Studios = 1;

            var service = Service();

            service.Start(V2Choice(SceneMetadataType.StashDb));

            var progress = Await(service);

            progress.ErrorKey.Should().BeNull();
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Once);
            _finishedState.SourceKind.Should().Be(LegacyKind.V2);
            _finishedState.SceneSource.Should().Be(SceneMetadataType.StashDb);
            _finishedState.Phase.Should().Be("Phase2Pending");
        }

        [Test]
        public void v2_without_series_should_be_completed_and_skip_preflight()
        {
            _counts.Series = 0;
            _install.Studios = 0;

            var progress = RunV2();

            _preflight.Verify(p => p.Check(), Times.Never);
            _finishedState.Phase.Should().Be("Done");
            _finishedState.Completed.Should().BeTrue();
            _finishedState.CompletedAt.Should().NotBeNull();
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("skipped");
        }

        [Test]
        public void v2_porter_open_failure_on_postgres_should_be_attributed_to_port_settings()
        {
            _install.Database.Type = DatabaseType.PostgreSQL;
            _install.Database.PostgresMainDb = "whisparr-main";
            _porter.Setup(p => p.Open(It.IsAny<LegacyInstall>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<DatabaseType>()))
                .Throws(new InvalidOperationException("cannot open the v2 database"));

            var progress = RunV2();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("Unexpected");
            progress.CanRetry.Should().BeFalse();
            progress.Steps.Single(s => s.Step == SetupStep.PortSettings).State.Should().Be("failed");
            progress.Steps.Single(s => s.Step == SetupStep.CreateDatabase).State.Should().Be("done");
        }

        [Test]
        public void porter_failure_should_discard_sidecar_items()
        {
            _porter.Setup(p => p.PortSettings(It.IsAny<V2PortContext>())).Throws(new InvalidOperationException("bad row"));

            var progress = RunV2();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("Unexpected");
            progress.CanRetry.Should().BeFalse();
            progress.ErrorMessage.Should().Contain("bad row");
            progress.Steps.Single(s => s.Step == SetupStep.PortSettings).State.Should().Be("failed");
            _runState.Verify(r => r.DiscardIfPresent(), Times.Once);
            _runState.Verify(r => r.Complete(), Times.Never);
            _lifecycle.Verify(l => l.Restart(), Times.Never);
            _calls.Should().Contain("dispose");
            File.Exists(Path.Combine(_appData, "whisparr5.db")).Should().BeFalse();
        }

        [Test]
        public void eros_should_skip_the_v2_steps()
        {
            var progress = Run();

            progress.Steps.Single(s => s.Step == SetupStep.PortSettings).State.Should().Be("skipped");
            progress.Steps.Single(s => s.Step == SetupStep.SnapshotLibrary).State.Should().Be("skipped");
            _porter.Verify(p => p.Open(It.IsAny<LegacyInstall>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<DatabaseType>()), Times.Never);
        }

        [Test]
        public void eros_source_change_runs_seeder_and_sets_rebuild()
        {
            _seeded.Scenes = 3;

            var service = Service();

            service.Start(new SetupChoice { Kind = LegacyKind.Eros, SceneSource = SceneMetadataType.Tpdb, MovieSource = MovieMetadataType.TPDB });

            var progress = Await(service);

            _calls.Should().ContainInOrder("preflight", "backup", "resolve", "seedscenes", "finish");
            progress.Steps.Single(s => s.Step == SetupStep.SceneSeeds).State.Should().Be("done");
            _finishedState.Rebuild.Should().BeTrue();
            _finishedState.Phase.Should().Be("Phase2Pending");
            _finishedState.Completed.Should().BeFalse();
        }

        [Test]
        public void eros_source_change_with_nothing_to_seed_completes()
        {
            var service = Service();

            service.Start(new SetupChoice { Kind = LegacyKind.Eros, SceneSource = SceneMetadataType.Tpdb, MovieSource = MovieMetadataType.TPDB });

            Await(service);

            _calls.Should().Contain("seedscenes");
            _finishedState.Rebuild.Should().BeTrue();
            _finishedState.Phase.Should().Be("Done");
        }

        [Test]
        public void eros_same_source_does_not_rebuild()
        {
            Run();

            _finishedState.Rebuild.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_valid_choice_after_a_refusal()
        {
            var service = Service();

            service.Start(new SetupChoice { Kind = LegacyKind.Eros, SceneSource = SceneMetadataType.Tpdb, MovieSource = MovieMetadataType.TPDB });
            service.Start(Choice());

            var progress = Await(service);

            progress.Status.Should().Be(SetupStatus.Restarting);
            progress.ErrorKey.Should().BeNull();
            progress.ErrorMessage.Should().BeNull();
        }

        [Test]
        public void start_and_retry_should_be_refused_after_the_migration_was_committed()
        {
            _runState.Setup(r => r.Complete()).Throws(new IOException("sidecar is locked"));

            var service = Service();

            Run(service).Status.Should().Be(SetupStatus.Failed);

            service.Start(Choice());
            service.Retry();

            var progress = Await(service);

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.CanRetry.Should().BeFalse();
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Once);
            _runState.Verify(r => r.DiscardIfPresent(), Times.Never);
        }

        [Test]
        public void should_keep_an_existing_pre_setup_copy_of_config5()
        {
            _seededConfig5 = false;
            File.WriteAllText(Path.Combine(_appData, "config5.xml"), "<Config>half-restored</Config>");

            var copyPath = Path.Combine(_appData, "config5.xml.pre-v5-setup");

            File.WriteAllText(copyPath, "<Config>users</Config>");

            Run();

            File.ReadAllText(copyPath).Should().Be("<Config>users</Config>");
            _runState.Verify(r => r.RecordConfig5Backup(copyPath), Times.Once);
        }

        [Test]
        public void should_refuse_a_choice_with_no_matching_install()
        {
            _detection.Installs.Clear();

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Idle);
            progress.ErrorKey.Should().Be("Unexpected");
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_refuse_an_install_that_cannot_be_migrated()
        {
            _install.Refusal = LegacyRefusal.ErosNewerThanV5;

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Idle);
            progress.ErrorKey.Should().Be("Unexpected");
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_list_env_vars_to_change()
        {
            _targets.EnvVarsStillNamingLegacy = new List<string> { "Whisparr__Postgres__MainDb", "Whisparr__Postgres__LogDb" };

            var progress = Run();

            progress.EnvVarsToChange.Should().Equal("Whisparr__Postgres__MainDb", "Whisparr__Postgres__LogDb");
        }

        [Test]
        public void should_not_restart_when_env_still_names_legacy()
        {
            _targets.EnvVarsStillNamingLegacy = new List<string>
            {
                "Whisparr__Postgres__MainDb",
                "Whisparr__Postgres__LogDbConnectionString"
            };

            _options = new PostgresOptions
            {
                MainDb = "whisparr-main",
                LogDbConnectionString = "Host=db;Port=5432;Username=u;Database=whisparr-log"
            };

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Succeeded);
            _lifecycle.Verify(l => l.Restart(), Times.Never);
            _calls.Should().Contain("complete");

            progress.EnvTargets.Select(t => t.Name).Should().Equal("Whisparr__Postgres__MainDb", "Whisparr__Postgres__LogDbConnectionString");
            progress.EnvTargets[0].Value.Should().Be("whisparr5-main");
            progress.EnvTargets[1].Value.Should().Contain("Database=whisparr5-log");
            progress.EnvTargets[1].Value.Should().Contain("Host=db");
            progress.EnvTargets[1].Value.Should().NotContain("whisparr-log");
        }

        [Test]
        public void retry_should_rerun_from_backup()
        {
            _install.HasMixedMovies = true;
            _mixed.Detached = 1;
            _preflight.Setup(p => p.Check()).Throws(new SetupStepException("Skyhook didn't answer"));

            var service = Service();
            var progress = Run(service);

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("SkyhookUnavailable");
            progress.CanRetry.Should().BeTrue();
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("failed");
            _runState.Verify(r => r.DiscardIfPresent(), Times.Never);
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);

            _preflight.Setup(p => p.Check()).Callback(() => _calls.Add("preflight"));
            _backup.Setup(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()))
                .Callback(() => _calls.Add("backup"))
                .Returns("~/second.zip");

            service.Retry();
            progress = Await(service);

            progress.Status.Should().Be(SetupStatus.Restarting);
            progress.BackupPath.Should().Be("~/second.zip");
            progress.ErrorKey.Should().BeNull();
            progress.ErrorMessage.Should().BeNull();
            _calls.Count(c => c == "backup").Should().Be(1);
        }

        [Test]
        public void preflight_failure_stops_before_backup()
        {
            _install.HasMixedMovies = true;
            _preflight.Setup(p => p.Check()).Throws(new SetupStepException("Skyhook didn't answer"));

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("SkyhookUnavailable");
            progress.CanRetry.Should().BeTrue();
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("failed");
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);
            _runState.Verify(r => r.Begin(It.IsAny<bool>()), Times.Never);
            _runState.Verify(r => r.DiscardIfPresent(), Times.Never);
        }

        [Test]
        public void preflight_skipped_for_eros_same_source_without_mixed_movies()
        {
            var progress = Run();

            _preflight.Verify(p => p.Check(), Times.Never);
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("skipped");
        }

        [Test]
        public void preflight_runs_for_v2_with_studios()
        {
            _install.Studios = 2;

            var progress = RunV2();

            _preflight.Verify(p => p.Check(), Times.Once);
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("done");
        }

        [Test]
        public void preflight_is_first_step()
        {
            Run().Steps[0].Step.Should().Be(SetupStep.SkyhookPreflight);
        }

        [TestCase(LegacyKind.V2, 3, SceneMetadataType.Tpdb, SceneMetadataType.Tpdb, false, true)]
        [TestCase(LegacyKind.V2, 0, SceneMetadataType.Tpdb, SceneMetadataType.Tpdb, true, false)]
        [TestCase(LegacyKind.Eros, 0, SceneMetadataType.Tpdb, SceneMetadataType.StashDb, false, true)]
        [TestCase(LegacyKind.Eros, 0, SceneMetadataType.StashDb, SceneMetadataType.StashDb, true, true)]
        [TestCase(LegacyKind.Eros, 0, SceneMetadataType.StashDb, SceneMetadataType.StashDb, false, false)]
        public void needs_skyhook_table(LegacyKind kind, int studios, SceneMetadataType chosen, SceneMetadataType previous, bool mixed, bool expected)
        {
            var install = new LegacyInstall { Kind = kind, Studios = studios, PreviousSceneSource = previous, HasMixedMovies = mixed };
            var choice = new SetupChoice { Kind = kind, SceneSource = chosen };

            SetupWizardService.NeedsSkyhook(choice, install).Should().Be(expected);
        }

        [Test]
        public void retry_should_do_nothing_when_the_failure_cannot_be_retried()
        {
            _backup.Setup(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()))
                .Throws(new SetupStepException("nope"));

            var service = Service();
            Run(service);

            service.Retry();

            service.Progress.Status.Should().Be(SetupStatus.Failed);
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void start_should_be_ignored_while_running()
        {
            using var release = new ManualResetEventSlim();
            using var started = new ManualResetEventSlim();

            _backup.Setup(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()))
                .Callback(() =>
                {
                    started.Set();
                    release.Wait(TimeSpan.FromSeconds(30));
                })
                .Returns("~/backup.zip");

            var service = Service();
            service.Start(Choice());
            started.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

            service.Progress.Status.Should().Be(SetupStatus.Running);
            service.Start(Choice());

            release.Set();
            Await(service);

            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void should_show_the_env_switch_instructions_for_a_pending_env_switch()
        {
            _detection.Installs.Clear();
            _detection.PendingEnvSwitch = true;
            _detection.EnvTargets.Add(new EnvTarget { Name = "Whisparr__Postgres__MainDb", Value = "whisparr5-main" });

            var progress = Service().Progress;

            progress.Status.Should().Be(SetupStatus.Succeeded);
            progress.EnvVarsToChange.Should().Equal("Whisparr__Postgres__MainDb");
            progress.EnvTargets.Should().ContainSingle().Which.Value.Should().Be("whisparr5-main");
        }

        [Test]
        public void should_keep_a_copy_of_a_config5_the_wizard_did_not_create()
        {
            _seededConfig5 = false;
            File.WriteAllText(Path.Combine(_appData, "config5.xml"), "<Config>users</Config>");

            var copyPath = Path.Combine(_appData, "config5.xml.pre-v5-setup");
            string copiedContent = null;

            // The copy has to exist by the time the seeder overwrites the file.
            _seeder.Setup(s => s.Seed(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LegacyInstall>()))
                .Callback(() => copiedContent = File.Exists(copyPath) ? File.ReadAllText(copyPath) : null)
                .Returns(() => _targets);

            Run();

            copiedContent.Should().Be("<Config>users</Config>");
            _runState.Verify(r => r.RecordConfig5Backup(copyPath), Times.Once);
        }

        [Test]
        public void should_not_copy_a_config5_the_wizard_created()
        {
            File.WriteAllText(Path.Combine(_appData, "config5.xml"), "<Config>seeded</Config>");

            Run();

            _runState.Verify(r => r.RecordConfig5Backup(It.IsAny<string>()), Times.Never);
            File.Exists(Path.Combine(_appData, "config5.xml.pre-v5-setup")).Should().BeFalse();
        }

        [Test]
        public void failure_after_the_database_is_in_place_should_end_in_a_terminal_status_without_discarding()
        {
            _runState.Setup(r => r.Complete()).Throws(new IOException("sidecar is locked"));

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("Unexpected");
            progress.CanRetry.Should().BeFalse();
            progress.ErrorMessage.Should().Contain("v5 database was built").And.Contain("Restart Whisparr");
            _runState.Verify(r => r.DiscardIfPresent(), Times.Never);
            _lifecycle.Verify(l => l.Restart(), Times.Never);
            File.Exists(Path.Combine(_appData, "whisparr5.db")).Should().BeTrue();
        }

        [Test]
        public void failure_while_locking_sources_should_mark_the_running_steps_failed()
        {
            _finisher.Setup(f => f.Finish(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<SceneMetadataType>(), It.IsAny<MovieMetadataType>(), It.IsAny<LegacyMigrationState>()))
                .Throws(new InvalidOperationException("locked"));

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.Steps.Single(s => s.Step == SetupStep.LockSources).State.Should().Be("failed");
            progress.Steps.Single(s => s.Step == SetupStep.Finish).State.Should().Be("failed");
            progress.Steps.Should().NotContain(s => s.State == "running");
        }

        [Test]
        public void start_should_be_ignored_once_succeeded_or_restarting()
        {
            var service = Service();

            Run(service).Status.Should().Be(SetupStatus.Restarting);

            service.Start(Choice());

            service.Progress.Status.Should().Be(SetupStatus.Restarting);
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void start_should_be_ignored_for_a_pending_env_switch()
        {
            _detection.PendingEnvSwitch = true;
            _detection.EnvTargets.Add(new EnvTarget { Name = "Whisparr__Postgres__MainDb", Value = "whisparr5-main" });

            var service = Service();

            service.Start(Choice());

            service.Progress.Status.Should().Be(SetupStatus.Succeeded);
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void shut_down_should_stop_the_app_without_changing_data()
        {
            Service().ShutDown();

            _lifecycle.Verify(l => l.Shutdown(), Times.Once);
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);
        }
    }
}
