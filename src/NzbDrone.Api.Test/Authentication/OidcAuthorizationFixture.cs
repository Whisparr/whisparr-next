using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
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
    public class OidcAuthorizationFixture : TestBase
    {
        private const string ConfiguredUser = "user@example.com";

        private Mock<IConfigFileProvider> _config;

        [SetUp]
        public void SetupConfig()
        {
            _config = new Mock<IConfigFileProvider>();
            _config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            _config.SetupGet(c => c.OidcAuthority).Returns("https://idp.example.com");
            _config.SetupGet(c => c.OidcClientId).Returns("whisparr");
            _config.SetupGet(c => c.OidcClientSecret).Returns("s3cret");
            _config.SetupGet(c => c.OidcUserIdentifier).Returns(ConfiguredUser);
            _config.SetupGet(c => c.OidcScopes).Returns("openid profile");
        }

        private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        {
            var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test");

            return new ClaimsPrincipal(identity);
        }

        private static JsonElement UserInfo(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        // Authorization

        [Test]
        public void should_authorize_verified_email_claim()
        {
            var principal = Principal(("email", ConfiguredUser), ("email_verified", "true"));

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, null).Should().BeTrue();

            principal.FindFirst("user")?.Value.Should().Be(ConfiguredUser);
            principal.FindFirst("AuthenticationType")?.Value.Should().Be("Oidc");
        }

        [Test]
        public void should_authorize_subject_claim()
        {
            var principal = Principal(("sub", ConfiguredUser));

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, null).Should().BeTrue();
        }

        [Test]
        public void should_match_case_insensitively()
        {
            var principal = Principal(("email", "USER@Example.COM"), ("email_verified", "true"));

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, null).Should().BeTrue();
        }

        [Test]
        public void should_not_authorize_unverified_email_claim()
        {
            var principal = Principal(("email", ConfiguredUser), ("email_verified", "false"));

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, null).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_authorize_email_without_verified_claim()
        {
            var principal = Principal(("email", ConfiguredUser));

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, null).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_authorize_when_userinfo_says_email_is_unverified()
        {
            // The claim on the principal says verified, but the user-info endpoint wins.
            var principal = Principal(("email", ConfiguredUser), ("email_verified", "true"));
            var userInfo = UserInfo($"{{\"email\":\"{ConfiguredUser}\",\"email_verified\":false}}");

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, userInfo).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_authorize_userinfo_email_without_verified_flag()
        {
            var userInfo = UserInfo($"{{\"email\":\"{ConfiguredUser}\"}}");

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, Principal(), userInfo).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase("true")]
        [TestCase("\"true\"")]
        public void should_authorize_verified_userinfo_email(string verified)
        {
            var userInfo = UserInfo($"{{\"email\":\"{ConfiguredUser}\",\"email_verified\":{verified}}}");

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, Principal(), userInfo).Should().BeTrue();
        }

        [Test]
        public void should_authorize_userinfo_subject()
        {
            var userInfo = UserInfo($"{{\"sub\":\"{ConfiguredUser}\"}}");

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, Principal(), userInfo).Should().BeTrue();
        }

        [Test]
        public void should_fall_back_to_principal_email_when_userinfo_has_none()
        {
            var principal = Principal(("email", ConfiguredUser), ("email_verified", "true"));
            var userInfo = UserInfo("{\"name\":\"Someone\"}");

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, userInfo).Should().BeTrue();
        }

        [Test]
        public void should_not_authorize_other_user()
        {
            var principal = Principal(("email", "other@example.com"), ("email_verified", "true"), ("sub", "abc"));

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, null).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase(null)]
        public void should_reject_when_no_user_is_configured(string configured)
        {
            _config.SetupGet(c => c.OidcUserIdentifier).Returns(configured);

            var principal = Principal(("sub", "abc"), ("email", "abc"), ("email_verified", "true"));

            AuthenticationBuilderExtensions.TryAuthorize(_config.Object, principal, null).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        // Cookie validation

        private static (CookieValidatePrincipalContext Context, Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService> Auth) GivenCookie(string user)
        {
            var auth = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();

            auth.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);

            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider()
            };

            var ticket = new AuthenticationTicket(Principal(("user", user)), "Cookies");
            var scheme = new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler));

            return (new CookieValidatePrincipalContext(httpContext, scheme, new CookieAuthenticationOptions(), ticket), auth);
        }

        [Test]
        public async Task should_keep_cookie_for_configured_user()
        {
            var (context, auth) = GivenCookie("USER@example.com");

            await AuthenticationBuilderExtensions.ValidateOidcPrincipal(context, _config.Object);

            context.Principal.Should().NotBeNull();
            auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()), Times.Never());
        }

        [Test]
        public async Task should_reject_cookie_when_user_changed()
        {
            var (context, auth) = GivenCookie("someone.else@example.com");

            await AuthenticationBuilderExtensions.ValidateOidcPrincipal(context, _config.Object);

            context.Principal.Should().BeNull();
            auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Cookies", It.IsAny<AuthenticationProperties>()), Times.Once());
        }

        [TestCase(AuthenticationType.Forms)]
        [TestCase(AuthenticationType.None)]
        public async Task should_reject_cookie_when_method_is_no_longer_oidc(AuthenticationType method)
        {
            _config.SetupGet(c => c.AuthenticationMethod).Returns(method);

            var (context, auth) = GivenCookie(ConfiguredUser);

            await AuthenticationBuilderExtensions.ValidateOidcPrincipal(context, _config.Object);

            context.Principal.Should().BeNull();
            auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "Cookies", It.IsAny<AuthenticationProperties>()), Times.Once());
        }

        [Test]
        public async Task should_reject_cookie_when_oidc_is_no_longer_configured()
        {
            _config.SetupGet(c => c.OidcClientSecret).Returns(string.Empty);

            var (context, _) = GivenCookie(ConfiguredUser);

            await AuthenticationBuilderExtensions.ValidateOidcPrincipal(context, _config.Object);

            context.Principal.Should().BeNull();
        }

        [Test]
        public async Task should_reject_cookie_when_user_is_cleared()
        {
            _config.SetupGet(c => c.OidcUserIdentifier).Returns(string.Empty);

            var (context, _) = GivenCookie(string.Empty);

            await AuthenticationBuilderExtensions.ValidateOidcPrincipal(context, _config.Object);

            context.Principal.Should().BeNull();
        }
    }
}
