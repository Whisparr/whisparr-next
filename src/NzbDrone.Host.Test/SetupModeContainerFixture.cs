using System;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Host;
using NzbDrone.Host.AccessControl;
using NzbDrone.Test.Common;
using Whisparr.Http.ErrorManagement;
using IServiceProvider = System.IServiceProvider;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class SetupModeContainerFixture : TestBase
    {
        private IServiceProvider _services;
        private ISetupModeState _state;

        [SetUp]
        public void SetUp()
        {
            var detection = new DetectionResult { SetupModeRequired = true };
            detection.Installs.Add(new LegacyInstall
            {
                Kind = LegacyKind.Eros,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = "/nonexistent/whisparr3.db" }
            });

            _state = new SetupModeState(detection, true);

            // The same registrations Bootstrap makes for setup mode.
            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(Bootstrap.ASSEMBLIES)
                .AddNzbDroneLogger()
                .AddDummyDatabase()
                .AddDummyLogDatabase()
                .AddSetupMode(_state)
                .AddStartupContext(new StartupContext("first", "second"));

            container.RegisterInstance<IHostLifetime>(new Mock<IHostLifetime>().Object);
            container.RegisterInstance<IOptions<PostgresOptions>>(new Mock<IOptions<PostgresOptions>>().Object);
            container.RegisterInstance<IOptions<AuthOptions>>(new Mock<IOptions<AuthOptions>>().Object);
            container.RegisterInstance<IOptions<AppOptions>>(new Mock<IOptions<AppOptions>>().Object);
            container.RegisterInstance<IOptions<ServerOptions>>(new Mock<IOptions<ServerOptions>>().Object);
            container.RegisterInstance<IOptions<UpdateOptions>>(new Mock<IOptions<UpdateOptions>>().Object);
            container.RegisterInstance<IOptions<LogOptions>>(new Mock<IOptions<LogOptions>>().Object);

            _services = container.GetServiceProvider();
        }

        [Test]
        public void should_resolve_user_service_as_legacy_user_service()
        {
            _services.GetRequiredService<IUserService>().Should().BeOfType<LegacyUserService>();
        }

        [Test]
        public void should_resolve_the_setup_wizard_service_with_every_dependency()
        {
            var wizard = _services.GetRequiredService<ISetupWizardService>();

            wizard.Should().BeOfType<SetupWizardService>();
            wizard.Progress.Status.Should().Be(SetupStatus.Idle);
        }

        [Test]
        public void should_resolve_the_registered_setup_state()
        {
            _services.GetRequiredService<ISetupModeState>().Should().BeSameAs(_state);
            _state.IsActive.Should().BeTrue();
            _state.SeededConfig5.Should().BeTrue();
        }

        [Test]
        public void should_resolve_configure_and_lifetime_dependencies_without_opening_a_database()
        {
            var types = new[]
            {
                typeof(IStartupContext),
                typeof(Lazy<IMainDatabase>),
                typeof(Lazy<ILogDatabase>),
                typeof(DatabaseTarget),
                typeof(ISingleInstancePolicy),
                typeof(InitializeLogger),
                typeof(ReconfigureLogging),
                typeof(IAppFolderFactory),
                typeof(IProvidePidFile),
                typeof(IConfigFileProvider),
                typeof(IRuntimeInfo),
                typeof(IFirewallAdapter),
                typeof(IEventAggregator),
                typeof(WhisparrErrorPipeline),
                typeof(IAnalyticsService),
                typeof(ISetupModeState)
            };

            foreach (var type in types)
            {
                _services.GetRequiredService(type).Should().NotBeNull(type.Name);
            }
        }
    }
}
