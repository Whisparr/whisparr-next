using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Whisparr.Api.V3.Config;

namespace NzbDrone.Api.Test.v3.Config
{
    // The V3 API is frozen: it neither exposes nor writes the OIDC settings.
    [TestFixture]
    public class HostConfigControllerOidcFixture : TestBase<HostConfigController>
    {
        [Test]
        public void put_should_not_write_oidc_keys()
        {
            Dictionary<string, object> saved = null;

            Mocker.GetMock<IConfigFileProvider>()
                  .Setup(c => c.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                  .Callback<Dictionary<string, object>>(d => saved = d);

            Subject.SaveHostConfig(new HostConfigResource
            {
                Id = 1,
                AuthenticationMethod = AuthenticationType.Forms
            });

            saved.Should().NotBeNull();
            saved.Keys.Where(k => k.StartsWith("Oidc")).Should().BeEmpty();
        }
    }
}
