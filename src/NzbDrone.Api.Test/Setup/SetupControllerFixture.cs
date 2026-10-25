using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using Whisparr.Http.Setup;

namespace NzbDrone.Api.Test.Setup
{
    [TestFixture]
    public class SetupControllerFixture
    {
        private Mock<ISetupWizardService> _wizard;
        private Mock<IConfig5Seeder> _seeder;
        private DetectionResult _detection;
        private SetupProgress _progress;

        [SetUp]
        public void Setup()
        {
            _wizard = new Mock<ISetupWizardService>();
            _seeder = new Mock<IConfig5Seeder>();
            _progress = new SetupProgress { Status = SetupStatus.Idle };
            _detection = new DetectionResult { SetupModeRequired = true, LegacyConfigPath = "config.xml" };

            _wizard.SetupGet(w => w.Progress).Returns(() => _progress);
        }

        private SetupController Subject() => new(new SetupModeState(_detection, false), _wizard.Object, _seeder.Object);

        private SetupStatusResource Status()
        {
            var result = Subject().GetStatus();

            return result.Value;
        }

        private static LegacyInstall Install(LegacyKind kind, MovieMetadataType previousMovie = MovieMetadataType.NONE)
        {
            return new LegacyInstall
            {
                Kind = kind,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite },
                PreviousMovieSource = previousMovie,
                Scenes = 3,
                Movies = 2
            };
        }

        [Test]
        public void status_should_return_defaults_for_eros()
        {
            _detection.Installs.Add(Install(LegacyKind.Eros, MovieMetadataType.TPDB));

            var status = Status();

            status.Installs.Should().ContainSingle();
            status.Installs[0].Scenes.Should().Be(3);
            status.Defaults.Kind.Should().Be(LegacyKind.Eros);
            status.Defaults.SceneSource.Should().Be(SceneMetadataType.StashDb);
            status.Defaults.MovieSource.Should().Be(MovieMetadataType.TPDB);
        }

        [Test]
        public void status_should_default_none_movie_source_to_tmdb()
        {
            _detection.Installs.Add(Install(LegacyKind.Eros));

            Status().Defaults.MovieSource.Should().Be(MovieMetadataType.TMDB);
        }

        [Test]
        public void status_should_default_to_eros_when_both()
        {
            _detection.Installs.Add(Install(LegacyKind.V2));
            _detection.Installs.Add(Install(LegacyKind.Eros));

            Status().Defaults.Kind.Should().Be(LegacyKind.Eros);
        }

        [Test]
        public void status_should_default_to_tpdb_for_v2()
        {
            _detection.Installs.Add(Install(LegacyKind.V2));

            var defaults = Status().Defaults;

            defaults.Kind.Should().Be(LegacyKind.V2);
            defaults.SceneSource.Should().Be(SceneMetadataType.Tpdb);
            defaults.MovieSource.Should().Be(MovieMetadataType.TPDB);
        }

        [Test]
        public void status_should_expose_errors_and_env_targets()
        {
            _detection.Errors.Add("whisparr2.db could not be read");
            _detection.PendingEnvSwitch = true;
            _detection.EnvTargets.Add(new EnvTarget { Name = "Whisparr__Postgres__MainDb", Value = "whisparr5-main" });

            var status = Status();

            status.Errors.Should().ContainSingle().Which.Should().Be("whisparr2.db could not be read");
            status.PendingEnvSwitch.Should().BeTrue();
            status.EnvTargets.Should().ContainSingle();
            status.EnvTargets[0].Name.Should().Be("Whisparr__Postgres__MainDb");
            status.EnvTargets[0].Value.Should().Be("whisparr5-main");
            _seeder.Verify(s => s.PlanTargets(It.IsAny<string>(), It.IsAny<LegacyInstall>()), Times.Never);
        }

        [Test]
        public void status_should_plan_env_targets_when_named_by_environment()
        {
            var install = Install(LegacyKind.Eros);
            install.Database.NamedByEnvironment = true;
            _detection.Installs.Add(install);

            _seeder.Setup(s => s.PlanTargets("config.xml", install)).Returns(new V5DatabaseTargets
            {
                EnvTargets = new List<EnvTarget> { new() { Name = "Whisparr__Postgres__MainDb", Value = "whisparr5-main" } }
            });

            var status = Status();

            status.EnvTargets.Should().ContainSingle().Which.Value.Should().Be("whisparr5-main");
            status.Installs[0].NamedByEnvironment.Should().BeTrue();
        }

        [Test]
        public void status_and_progress_json_should_not_contain_connection_string_password()
        {
            var targets = Config5Seeder.EnvTargetsFor(
                new[] { Config5Seeder.MainDbConnectionStringVariable },
                "whisparr5-main",
                "whisparr5-log",
                "Host=db;Database=whisparr-main;Username=u;Password=secret",
                null);

            _detection.Installs.Add(Install(LegacyKind.Eros));
            _detection.PendingEnvSwitch = true;
            _detection.EnvTargets.AddRange(targets);
            _progress = new SetupProgress { Status = SetupStatus.Succeeded, EnvTargets = targets };

            var status = Status();
            var json = NzbDrone.Common.Serializer.Json.ToJson(status);

            json.Should().NotContain("secret");
            json.Should().Contain("<unchanged>");
            NzbDrone.Common.Serializer.Json.ToJson(status.Progress).Should().NotContain("secret");
        }

        [Test]
        public void status_should_plan_env_targets_for_default_kind_when_both_named()
        {
            var v2 = Install(LegacyKind.V2);
            v2.Database.NamedByEnvironment = true;
            var eros = Install(LegacyKind.Eros);
            eros.Database.NamedByEnvironment = true;
            _detection.Installs.Add(v2);
            _detection.Installs.Add(eros);

            _seeder.Setup(s => s.PlanTargets("config.xml", eros)).Returns(new V5DatabaseTargets
            {
                EnvTargets = new List<EnvTarget> { new() { Name = "n", Value = "eros" } }
            });

            Status().EnvTargets.Should().ContainSingle().Which.Value.Should().Be("eros");
        }

        [Test]
        public void status_should_have_no_env_targets_when_config_is_unreadable()
        {
            var install = Install(LegacyKind.Eros);
            install.Database.NamedByEnvironment = true;
            _detection.Installs.Add(install);

            _seeder.Setup(s => s.PlanTargets(It.IsAny<string>(), It.IsAny<LegacyInstall>()))
                   .Throws(new InvalidConfigFileException("bad"));

            Status().EnvTargets.Should().BeEmpty();
        }

        [Test]
        public void status_should_map_progress()
        {
            _progress = new SetupProgress
            {
                Status = SetupStatus.Failed,
                CanRetry = true,
                ErrorKey = SetupErrorKey.BackupFailed,
                Steps = new List<(SetupStep Step, string State)> { (SetupStep.Backup, SetupStepState.Failed) }
            };
            _detection.Installs.Add(Install(LegacyKind.Eros));

            var progress = Status().Progress;

            progress.Status.Should().Be(SetupStatus.Failed);
            progress.CanRetry.Should().BeTrue();
            progress.Steps.Should().ContainSingle();
            progress.Steps[0].Step.Should().Be(SetupStep.Backup);
            progress.Steps[0].State.Should().Be(SetupStepState.Failed);
        }

        [Test]
        public void migrate_should_start_wizard()
        {
            _detection.Installs.Add(Install(LegacyKind.Eros));

            var result = Subject().Migrate(new SetupChoiceResource
            {
                Kind = LegacyKind.Eros,
                SceneSource = SceneMetadataType.Tpdb,
                MovieSource = MovieMetadataType.TMDB
            });

            result.Should().BeOfType<AcceptedResult>();
            _wizard.Verify(
                w => w.Start(It.Is<SetupChoice>(c => c.Kind == LegacyKind.Eros && c.SceneSource == SceneMetadataType.Tpdb && c.MovieSource == MovieMetadataType.TMDB)),
                Times.Once);
        }

        [Test]
        public void migrate_should_start_from_failed()
        {
            _detection.Installs.Add(Install(LegacyKind.Eros));
            _progress.Status = SetupStatus.Failed;

            Subject().Migrate(new SetupChoiceResource { Kind = LegacyKind.Eros }).Should().BeOfType<AcceptedResult>();
        }

        [Test]
        public void migrate_should_409_for_refused_install()
        {
            var install = Install(LegacyKind.V2);
            install.Refusal = LegacyRefusal.V2TooOld;
            _detection.Installs.Add(install);

            Subject().Migrate(new SetupChoiceResource { Kind = LegacyKind.V2 }).Should().BeOfType<ConflictObjectResult>();
            _wizard.Verify(w => w.Start(It.IsAny<SetupChoice>()), Times.Never);
        }

        [Test]
        public void migrate_should_409_when_kind_has_no_install()
        {
            _detection.Installs.Add(Install(LegacyKind.Eros));

            Subject().Migrate(new SetupChoiceResource { Kind = LegacyKind.V2 }).Should().BeOfType<ConflictObjectResult>();
        }

        [Test]
        public void migrate_should_409_when_pending_env_switch()
        {
            _detection.Installs.Add(Install(LegacyKind.Eros));
            _detection.PendingEnvSwitch = true;

            Subject().Migrate(new SetupChoiceResource { Kind = LegacyKind.Eros }).Should().BeOfType<ConflictObjectResult>();
            _wizard.Verify(w => w.Start(It.IsAny<SetupChoice>()), Times.Never);
        }

        [TestCase(SetupStatus.Running)]
        [TestCase(SetupStatus.Succeeded)]
        [TestCase(SetupStatus.Restarting)]
        public void migrate_should_409_when_not_idle_or_failed(SetupStatus status)
        {
            _detection.Installs.Add(Install(LegacyKind.Eros));
            _progress.Status = status;

            Subject().Migrate(new SetupChoiceResource { Kind = LegacyKind.Eros }).Should().BeOfType<ConflictObjectResult>();
            _wizard.Verify(w => w.Start(It.IsAny<SetupChoice>()), Times.Never);
        }

        [Test]
        public void retry_and_shutdown_should_call_wizard()
        {
            _detection.Installs.Add(Install(LegacyKind.Eros));
            var subject = Subject();

            subject.Retry().Should().BeOfType<AcceptedResult>();
            subject.ShutDown().Should().BeOfType<AcceptedResult>();

            _wizard.Verify(w => w.Retry(), Times.Once);
            _wizard.Verify(w => w.ShutDown(), Times.Once);
        }

        [Test]
        public void should_404_outside_setup_mode()
        {
            _detection = new DetectionResult();
            var subject = Subject();

            subject.GetStatus().Result.Should().BeOfType<NotFoundResult>();
            subject.Migrate(new SetupChoiceResource()).Should().BeOfType<NotFoundResult>();
            subject.Retry().Should().BeOfType<NotFoundResult>();
            subject.ShutDown().Should().BeOfType<NotFoundResult>();

            _wizard.Verify(w => w.Start(It.IsAny<SetupChoice>()), Times.Never);
            _wizard.Verify(w => w.Retry(), Times.Never);
            _wizard.Verify(w => w.ShutDown(), Times.Never);
        }
    }
}
