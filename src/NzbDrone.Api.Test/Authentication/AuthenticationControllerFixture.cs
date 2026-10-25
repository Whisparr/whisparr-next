using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Whisparr.Http.Authentication;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class AuthenticationControllerFixture : TestBase<AuthenticationController>
    {
        private Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService> _auth;

        [SetUp]
        public void Setup()
        {
            _auth = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
            _auth.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                 .Returns(Task.CompletedTask);

            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton(_auth.Object).BuildServiceProvider()
            };

            var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
                ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
            };

            Subject.ControllerContext = new ControllerContext(actionContext);
            Subject.Url = new UrlHelper(actionContext);

            GivenConfig(AuthenticationType.Oidc, true, string.Empty);
        }

        private void GivenConfig(AuthenticationType method, bool oidcConfigured, string urlBase)
        {
            var config = Mocker.GetMock<IConfigFileProvider>();

            config.SetupGet(c => c.AuthenticationMethod).Returns(method);
            config.SetupGet(c => c.UrlBase).Returns(urlBase);
            config.SetupGet(c => c.OidcAuthority).Returns(oidcConfigured ? "https://idp.example.com" : null);
            config.SetupGet(c => c.OidcClientId).Returns(oidcConfigured ? "whisparr" : null);
            config.SetupGet(c => c.OidcClientSecret).Returns(oidcConfigured ? "s3cret" : null);
            config.SetupGet(c => c.OidcUserIdentifier).Returns(oidcConfigured ? "user@example.com" : null);
            config.SetupGet(c => c.OidcScopes).Returns(oidcConfigured ? "openid" : null);
        }

        // LoginSso

        [Test]
        public void login_sso_should_challenge_the_oidc_scheme_when_configured()
        {
            var result = Subject.LoginSso("/series").Should().BeOfType<ChallengeResult>().Subject;

            result.AuthenticationSchemes.Should().BeEquivalentTo(new[] { "Oidc" });
            result.Properties.RedirectUri.Should().Be("/series");
        }

        [Test]
        public void login_sso_should_bounce_to_login_when_oidc_is_not_configured()
        {
            GivenConfig(AuthenticationType.Oidc, false, "/whisparr");

            Subject.LoginSso().Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/whisparr/login");

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void login_sso_should_bounce_to_login_when_method_is_not_oidc()
        {
            GivenConfig(AuthenticationType.Forms, true, string.Empty);

            Subject.LoginSso().Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/login");
        }

        // GetRedirectUrl, through LoginSso

        [TestCase("", null, "/")]
        [TestCase("", "/series", "/series")]
        [TestCase("/whisparr", null, "/whisparr/")]
        [TestCase("/whisparr", "https://evil.example.com", "/whisparr/")]
        [TestCase("/whisparr", "//evil.example.com", "/whisparr/")]
        [TestCase("/whisparr", "/whisparr", "/whisparr")]
        [TestCase("/whisparr", "/whisparr/series", "/whisparr/series")]
        [TestCase("/whisparr", "/WHISPARR/series", "/WHISPARR/series")]
        [TestCase("/whisparr", "/series", "/whisparr/series")]
        [TestCase("/whisparr", "/whisparrfoo", "/whisparr/whisparrfoo")]
        [TestCase("/whisparr", "/whisparrfoo/bar", "/whisparr/whisparrfoo/bar")]
        public void should_build_the_redirect_url(string urlBase, string returnUrl, string expected)
        {
            GivenConfig(AuthenticationType.Oidc, true, urlBase);

            Subject.LoginSso(returnUrl).Should().BeOfType<ChallengeResult>()
                   .Which.Properties.RedirectUri.Should().Be(expected);
        }

        // Logout

        [Test]
        public async Task logout_should_sign_out_of_the_provider_and_cookie_when_oidc()
        {
            var result = await Subject.Logout();

            result.Should().BeOfType<EmptyResult>();

            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Oidc", It.Is<AuthenticationProperties>(p => p.RedirectUri == "/loggedout")), Times.Once());
            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Cookies", It.IsAny<AuthenticationProperties>()), Times.Once());
            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Forms", It.IsAny<AuthenticationProperties>()), Times.Once());
        }

        [Test]
        public async Task logout_should_redirect_to_logged_out_page_when_provider_sign_out_fails()
        {
            GivenConfig(AuthenticationType.Oidc, true, "/whisparr");

            _auth.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Oidc", It.IsAny<AuthenticationProperties>()))
                 .ThrowsAsync(new InvalidOperationException("no end session endpoint"));

            var result = await Subject.Logout();

            result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/whisparr/loggedout");

            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Cookies", It.IsAny<AuthenticationProperties>()), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public async Task logout_should_use_forms_flow_when_oidc_falls_back_to_forms()
        {
            GivenConfig(AuthenticationType.Oidc, false, "/whisparr");

            var result = await Subject.Logout();

            result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/whisparr/");

            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Oidc", It.IsAny<AuthenticationProperties>()), Times.Never());
            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Forms", It.IsAny<AuthenticationProperties>()), Times.Once());
            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Cookies", It.IsAny<AuthenticationProperties>()), Times.Once());
        }

        [Test]
        public async Task logout_should_sign_out_both_cookies_for_forms()
        {
            GivenConfig(AuthenticationType.Forms, false, string.Empty);

            var result = await Subject.Logout();

            result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/");

            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Forms", It.IsAny<AuthenticationProperties>()), Times.Once());
            _auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Cookies", It.IsAny<AuthenticationProperties>()), Times.Once());
        }
    }
}
