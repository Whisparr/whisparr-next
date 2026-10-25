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
        private Mock<IMixedMovieResolver> _resolver;
        private Mock<ISkyhookPreflight> _preflight;
        private Mock<IV5DatabaseFinisher> _finisher;
        private Mock<ILifecycleService> _lifecycle;
        private LegacyInstall _install;
        private DetectionResult _detection;
        private V5DatabaseTargets _targets;
        private MixedMovieResult _mixed;
        private PostgresOptions _options;
        private LegacyMigrationState _finishedState;
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

            _resolver = new Mock<IMixedMovieResolver>();
            _resolver.Setup(r => r.Resolve(It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>(), It.IsAny<MovieMetadataType>()))
                .Callback(() => _calls.Add("resolve"))
                .Returns(() => _mixed);

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
                _resolver.Object,
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
            progress.Steps.Where(s => s.Step != SetupStep.SkyhookPreflight).Should().OnlyContain(s => s.State == "done");
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
        public void should_skip_preflight_without_detached_movies()
        {
            var progress = Run();

            _preflight.Verify(p => p.Check(), Times.Never);
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("skipped");
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
            _mixed.Detached = 2;

            var progress = Run();

            _calls.Should().ContainInOrder("resolve", "preflight", "finish");
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

        [Test]
        public void should_refuse_v2_as_not_available_yet()
        {
            var service = Service();

            service.Start(new SetupChoice { Kind = LegacyKind.V2, SceneSource = SceneMetadataType.Tpdb, MovieSource = MovieMetadataType.TPDB });

            AssertNotAvailable(service.Progress);
        }

        [Test]
        public void should_refuse_scene_source_change_as_not_available_yet()
        {
            var service = Service();

            service.Start(new SetupChoice { Kind = LegacyKind.Eros, SceneSource = SceneMetadataType.Tpdb, MovieSource = MovieMetadataType.TPDB });

            AssertNotAvailable(service.Progress);
        }

        private void AssertNotAvailable(SetupProgress progress)
        {
            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("SetupNotAvailableYet");
            progress.CanRetry.Should().BeFalse();
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_refuse_a_choice_with_no_matching_install()
        {
            _detection.Installs.Clear();

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("Unexpected");
            _backup.Verify(b => b.Backup(It.IsAny<LegacyInstall>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_refuse_an_install_that_cannot_be_migrated()
        {
            _install.Refusal = LegacyRefusal.ErosNewerThanV5;

            var progress = Run();

            progress.Status.Should().Be(SetupStatus.Failed);
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
            _mixed.Detached = 1;
            _preflight.Setup(p => p.Check()).Throws(new SetupStepException("Skyhook didn't answer"));

            var service = Service();
            var progress = Run(service);

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.ErrorKey.Should().Be("SkyhookUnavailable");
            progress.CanRetry.Should().BeTrue();
            progress.Steps.Single(s => s.Step == SetupStep.SkyhookPreflight).State.Should().Be("failed");
            _runState.Verify(r => r.DiscardIfPresent(), Times.Once);

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
            _calls.Count(c => c == "backup").Should().Be(2);
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
