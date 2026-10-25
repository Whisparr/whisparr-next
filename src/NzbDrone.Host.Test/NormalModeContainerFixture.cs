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
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Host;
using NzbDrone.Test.Common;
using IServiceProvider = System.IServiceProvider;

namespace NzbDrone.App.Test
{
    // The normal-mode path of Bootstrap: AddDatabase() must keep the auto-registered LegacyUserService
    // (and the SetupModeState class) from winning over UserService and the inactive state.
    [TestFixture]
    public class NormalModeContainerFixture : TestBase
    {
        private IServiceProvider _services;

        [SetUp]
        public void SetUp()
        {
            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(Bootstrap.ASSEMBLIES)
                .AddNzbDroneLogger()
                .AddDatabase()
                .AddStartupContext(new StartupContext("first", "second"));

            // AddDatabase builds the real database on first use. Keep that from opening a connection here.
            container.RegisterInstance<IMainDatabase>(new MainDatabase(null), ifAlreadyRegistered: IfAlreadyRegistered.Replace);

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
        public void should_resolve_user_service_as_user_service()
        {
            _services.GetRequiredService<IUserService>().Should().BeOfType<UserService>();
        }

        [Test]
        public void should_resolve_the_inactive_setup_state()
        {
            var state = _services.GetRequiredService<ISetupModeState>();

            state.Should().BeSameAs(SetupModeState.Inactive);
            state.IsActive.Should().BeFalse();
        }
    }
}
