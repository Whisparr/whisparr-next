using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class ConfigFileProviderOidcFixture : CoreTest<ConfigFileProvider>
    {
        private AuthOptions _authOptions;

        [SetUp]
        public void Setup()
        {
            _authOptions = new AuthOptions();

            Mocker.GetMock<IAppFolderInfo>()
                  .SetupGet(a => a.AppDataFolder)
                  .Returns("app-data");

            Mocker.SetConstant<IOptions<AuthOptions>>(Options.Create(_authOptions));

            GivenConfigFile("<Config><OidcAuthority>https://file.example</OidcAuthority></Config>");
        }

        private void GivenConfigFile(string contents)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(It.IsAny<string>()))
                  .Returns(contents);
        }

        [Test]
        public void should_prefer_auth_options_over_config_file()
        {
            _authOptions.OidcAuthority = "https://env.example";

            Subject.OidcAuthority.Should().Be("https://env.example");
        }

        [Test]
        public void should_read_config_file_when_no_auth_option()
        {
            Subject.OidcAuthority.Should().Be("https://file.example");
        }

        [Test]
        public void should_default_scopes()
        {
            Subject.OidcScopes.Should().Be("openid profile email");
        }
    }
}
