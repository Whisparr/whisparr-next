using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Http.Authentication;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class UiAuthorizationPolicyProviderFixture
    {
        private Mock<IConfigFileProvider> _config;
        private UiAuthorizationPolicyProvider _subject;

        [SetUp]
        public void Setup()
        {
            _config = new Mock<IConfigFileProvider>();
            _subject = new UiAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions()), _config.Object);
        }

        private void SetupOidc(string authority)
        {
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            _config.SetupGet(c => c.OidcAuthority).Returns(authority);
            _config.SetupGet(c => c.OidcClientId).Returns("whisparr");
            _config.SetupGet(c => c.OidcClientSecret).Returns("s3cret");
            _config.SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            _config.SetupGet(c => c.OidcScopes).Returns("openid profile email");
        }

        [Test]
        public async Task should_use_cookie_scheme_for_configured_oidc()
        {
            SetupOidc("https://idp.example.com");

            var policy = await _subject.GetPolicyAsync("UI");

            policy.AuthenticationSchemes.Should().BeEquivalentTo(new[] { "Cookies" });
        }

        [Test]
        public async Task should_use_forms_scheme_when_oidc_falls_back()
        {
            SetupOidc("http://idp.example.com");

            var policy = await _subject.GetPolicyAsync("UI");

            policy.AuthenticationSchemes.Should().BeEquivalentTo(new[] { "Forms" });
        }

        [Test]
        public async Task should_use_configured_scheme_for_forms()
        {
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Forms);

            var policy = await _subject.GetPolicyAsync("UI");

            policy.AuthenticationSchemes.Should().BeEquivalentTo(new[] { "Forms" });
        }
    }
}
