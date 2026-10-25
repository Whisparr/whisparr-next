using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class AuthenticationConfigurationExtensionsFixture : TestBase
    {
        private Mock<IConfigFileProvider> _config;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigFileProvider>();

            GivenOidcSettings("https://id.example", "c", "s", "u", "openid profile");
        }

        private void GivenOidcSettings(string authority, string clientId, string clientSecret, string userIdentifier, string scopes)
        {
            _config.SetupGet(c => c.OidcAuthority).Returns(authority);
            _config.SetupGet(c => c.OidcClientId).Returns(clientId);
            _config.SetupGet(c => c.OidcClientSecret).Returns(clientSecret);
            _config.SetupGet(c => c.OidcUserIdentifier).Returns(userIdentifier);
            _config.SetupGet(c => c.OidcScopes).Returns(scopes);
        }

        [Test]
        public void should_be_configured_with_all_settings_and_https_authority()
        {
            _config.Object.IsOidcConfigured().Should().BeTrue();
        }

        [Test]
        public void should_not_be_configured_with_http_authority()
        {
            GivenOidcSettings("http://id.example", "c", "s", "u", "openid profile");

            _config.Object.IsOidcConfigured().Should().BeFalse();
        }

        [Test]
        public void should_not_be_configured_without_openid_scope()
        {
            GivenOidcSettings("https://id.example", "c", "s", "u", "profile email");

            _config.Object.IsOidcConfigured().Should().BeFalse();
        }

        [Test]
        public void effective_method_should_fall_back_to_forms_when_oidc_incomplete()
        {
            GivenOidcSettings("https://id.example", "c", string.Empty, "u", "openid profile");
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);

            _config.Object.EffectiveAuthenticationMethod().Should().Be(AuthenticationType.Forms);
        }

        [Test]
        public void effective_method_should_stay_oidc_when_configured()
        {
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);

            _config.Object.EffectiveAuthenticationMethod().Should().Be(AuthenticationType.Oidc);
        }

        [Test]
        public void effective_method_should_pass_through_non_oidc()
        {
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.External);

            _config.Object.EffectiveAuthenticationMethod().Should().Be(AuthenticationType.External);
        }

        [Test]
        public void get_scopes_should_split_on_spaces_and_commas()
        {
            AuthenticationConfigurationExtensions.GetOidcScopes("openid, profile  email")
                .Should().Equal("openid", "profile", "email");

            AuthenticationConfigurationExtensions.GetOidcScopes(null).Should().BeEmpty();
        }
    }
}
