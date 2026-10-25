using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class OidcConfigurationCheckFixture : CoreTest<OidcConfigurationCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Message");

            GivenSettings(AuthenticationType.Oidc, "https://id.example", "c", "s", "u", "openid profile");
        }

        private void GivenSettings(AuthenticationType method, string authority, string clientId, string clientSecret, string userIdentifier, string scopes)
        {
            var config = Mocker.GetMock<IConfigFileProvider>();

            config.SetupGet(c => c.AuthenticationMethod).Returns(method);
            config.SetupGet(c => c.OidcAuthority).Returns(authority);
            config.SetupGet(c => c.OidcClientId).Returns(clientId);
            config.SetupGet(c => c.OidcClientSecret).Returns(clientSecret);
            config.SetupGet(c => c.OidcUserIdentifier).Returns(userIdentifier);
            config.SetupGet(c => c.OidcScopes).Returns(scopes);
        }

        private void GivenDiscoverable(bool discoverable)
        {
            Mocker.GetMock<IOidcDiscoveryService>()
                  .Setup(s => s.IsDiscoverable(It.IsAny<string>()))
                  .Returns(discoverable);
        }

        [Test]
        public void should_be_ok_when_method_is_not_oidc()
        {
            GivenSettings(AuthenticationType.Forms, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_error_not_configured_when_settings_missing()
        {
            GivenSettings(AuthenticationType.Oidc, "https://id.example", "c", string.Empty, "u", "openid profile");

            Subject.Check().ShouldBeError(reason: HealthCheckReason.OidcNotConfigured);
        }

        [Test]
        public void should_error_not_secure_for_http_authority()
        {
            GivenSettings(AuthenticationType.Oidc, "http://id.example", "c", "s", "u", "openid profile");

            Subject.Check().ShouldBeError(reason: HealthCheckReason.OidcAuthorityNotSecure);
        }

        [Test]
        public void should_error_discovery_failed_when_not_discoverable()
        {
            GivenDiscoverable(false);

            Subject.Check().ShouldBeError(reason: HealthCheckReason.OidcDiscoveryFailed);
        }

        [Test]
        public void should_be_ok_when_discoverable()
        {
            GivenDiscoverable(true);

            Subject.Check().ShouldBeOk();
        }
    }
}
