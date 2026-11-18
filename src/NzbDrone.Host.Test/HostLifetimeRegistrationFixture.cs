using System.Linq;
using DryIoc;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Host;
using NzbDrone.Test.Common;

namespace NzbDrone.App.Test
{
    // Bootstrap no longer calls UseWindowsService(). The assembly scan is the only thing that puts
    // RestartableServiceLifetime on the host, and without it in-process restarts and Windows services both break.
    [TestFixture]
    public class HostLifetimeRegistrationFixture : TestBase
    {
        [Test]
        public void should_register_the_restartable_lifetime_as_the_host_lifetime()
        {
            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(Bootstrap.ASSEMBLIES);

            container.GetServiceRegistrations()
                     .Where(r => r.ServiceType == typeof(IHostLifetime))
                     .Select(r => r.Factory.ImplementationType)
                     .Should().ContainSingle()
                     .Which.Should().Be<RestartableServiceLifetime>();
        }
    }
}
