using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Whisparr.Http.Frontend.Mappers;

namespace NzbDrone.Api.Test.Frontend.Mappers
{
    [TestFixture]
    public class LoginHtmlMapperFixture : TestBase
    {
        private void GivenOidc(string authority, string clientSecret)
        {
            var config = Mocker.GetMock<IConfigFileProvider>();

            config.SetupGet(c => c.Theme).Returns("auto");
            config.SetupGet(c => c.UrlBase).Returns(string.Empty);
            config.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Oidc);
            config.SetupGet(c => c.OidcAuthority).Returns(authority);
            config.SetupGet(c => c.OidcClientId).Returns("whisparr");
            config.SetupGet(c => c.OidcClientSecret).Returns(clientSecret);
            config.SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            config.SetupGet(c => c.OidcScopes).Returns("openid profile");
        }

        private string Render()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.StartUpFolder).Returns("/app");
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(It.IsAny<string>()))
                  .Returns("<body class=\"auth-_AUTH_METHOD_\"></body>");

            var subject = new ExposedLoginHtmlMapper(Mocker.GetMock<IAppFolderInfo>().Object,
                                                     Mocker.GetMock<IDiskProvider>().Object,
                                                     new Lazy<ICacheBreakerProvider>(() => Mocker.GetMock<ICacheBreakerProvider>().Object),
                                                     Mocker.GetMock<IConfigFileProvider>().Object,
                                                     LogManager.GetCurrentClassLogger());

            return subject.Render();
        }

        [Test]
        public void should_flag_misconfigured_when_oidc_selected_but_incomplete()
        {
            GivenOidc("https://id.example", string.Empty);

            Render().Should().Contain("auth-oidc oidc-misconfigured");
        }

        [Test]
        public void should_flag_misconfigured_when_oidc_authority_is_not_https()
        {
            GivenOidc("http://id.example", "secret");

            Render().Should().Contain("auth-oidc oidc-misconfigured");
        }

        [Test]
        public void should_not_flag_misconfigured_when_oidc_is_complete()
        {
            GivenOidc("https://id.example", "secret");

            Render().Should().Contain("auth-oidc").And.NotContain("oidc-misconfigured");
        }

        [Test]
        public void login_page_should_show_forms_login_and_hide_sso_button_when_misconfigured()
        {
            var html = File.ReadAllText(FindLoginHtml());

            Regex.IsMatch(html, @"\.oidc-misconfigured\s+\.forms-only\s*\{\s*display:\s*block;").Should().BeTrue();
            Regex.IsMatch(html, @"\.oidc-misconfigured\s+#sso-login\s*\{\s*display:\s*none;").Should().BeTrue();

            // The override has to come after the rule that hides forms for OIDC, equal specificity
            html.IndexOf(".oidc-misconfigured .forms-only", StringComparison.Ordinal)
                .Should().BeGreaterThan(html.IndexOf(".auth-oidc .forms-only", StringComparison.Ordinal));
        }

        private static string FindLoginHtml()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "frontend", "src", "login.html");

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("frontend/src/login.html not found above the test directory");
        }
    }

    internal sealed class ExposedLoginHtmlMapper : LoginHtmlMapper
    {
        public ExposedLoginHtmlMapper(IAppFolderInfo appFolderInfo,
                                      IDiskProvider diskProvider,
                                      Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                                      IConfigFileProvider configFileProvider,
                                      Logger logger)
            : base(appFolderInfo, diskProvider, cacheBreakProviderFactory, configFileProvider, logger)
        {
        }

        public string Render()
        {
            return GetHtmlText();
        }
    }
}
