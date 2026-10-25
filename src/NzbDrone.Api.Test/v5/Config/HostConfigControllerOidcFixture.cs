using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Config;

namespace NzbDrone.Api.Test.v5.Config
{
    [TestFixture]
    public class HostConfigControllerOidcFixture : TestBase<HostConfigControllerFixture.ValidatingHostConfigController>
    {
        private const string PrivateValue = "********";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrMovieMetadataSource)
                  .Returns(MovieMetadataType.TMDB);

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("locked");

            Mocker.GetMock<IOidcDiscoveryService>()
                  .Setup(s => s.IsDiscoverable(It.IsAny<string>()))
                  .Returns(true);
        }

        private void GivenStoredSecret(string secret)
        {
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(c => c.OidcClientSecret)
                  .Returns(secret);
        }

        private static HostConfigResource OidcResource(string authority = "https://idp.example.com", string scopes = "openid profile email")
        {
            return new HostConfigResource
            {
                Id = 1,
                AuthenticationMethod = AuthenticationType.Oidc,
                OidcAuthority = authority,
                OidcClientId = "whisparr",
                OidcClientSecret = PrivateValue,
                OidcUserIdentifier = "user@example.com",
                OidcScopes = scopes
            };
        }

        [Test]
        public void get_should_mask_client_secret()
        {
            GivenStoredSecret("s3cret");

            Subject.GetHostConfig().OidcClientSecret.Should().Be(PrivateValue);
        }

        [Test]
        public void get_should_return_empty_when_no_secret()
        {
            GivenStoredSecret(null);

            Subject.GetHostConfig().OidcClientSecret.Should().BeEmpty();
        }

        [Test]
        public void get_should_return_oidc_settings()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcAuthority).Returns("https://idp.example.com");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcClientId).Returns("whisparr");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcScopes).Returns("openid");

            var resource = Subject.GetHostConfig();

            resource.OidcAuthority.Should().Be("https://idp.example.com");
            resource.OidcClientId.Should().Be("whisparr");
            resource.OidcUserIdentifier.Should().Be("user@example.com");
            resource.OidcScopes.Should().Be("openid");
        }

        [Test]
        public void put_should_keep_stored_secret_when_placeholder_sent()
        {
            GivenStoredSecret("s3cret");

            Subject.SaveHostConfig(OidcResource());

            Mocker.GetMock<IConfigFileProvider>()
                  .Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d => (string)d["OidcClientSecret"] == "s3cret")), Times.Once());
        }

        [Test]
        public void put_should_reject_placeholder_secret_when_none_is_stored()
        {
            GivenStoredSecret(string.Empty);

            var failures = Subject.Validator.Validate(OidcResource()).Errors;

            failures.Should().Contain(e => e.PropertyName == nameof(HostConfigResource.OidcClientSecret));
        }

        [Test]
        public void put_should_accept_placeholder_secret_when_one_is_stored()
        {
            GivenStoredSecret("s3cret");

            var failures = Subject.Validator.Validate(OidcResource()).Errors;

            failures.Should().NotContain(e => e.PropertyName == nameof(HostConfigResource.OidcClientSecret));
        }

        [Test]
        public void put_should_persist_new_secret()
        {
            GivenStoredSecret("s3cret");

            var resource = OidcResource();
            resource.OidcClientSecret = "new-secret";

            Subject.SaveHostConfig(resource);

            Mocker.GetMock<IConfigFileProvider>()
                  .Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d => (string)d["OidcClientSecret"] == "new-secret")), Times.Once());
        }

        [Test]
        public void put_should_reject_http_authority_for_oidc()
        {
            var failures = Subject.Validator.Validate(OidcResource("http://idp.example.com")).Errors;

            failures.Should().Contain(e => e.PropertyName == nameof(HostConfigResource.OidcAuthority) &&
                                           e.ErrorMessage == "OIDC Authority must start with 'https://'");
        }

        [Test]
        public void put_should_reject_scopes_without_openid()
        {
            var failures = Subject.Validator.Validate(OidcResource(scopes: "profile email")).Errors;

            failures.Should().Contain(e => e.PropertyName == nameof(HostConfigResource.OidcScopes) &&
                                           e.ErrorMessage == "OIDC Scopes must include 'openid'");
        }

        [Test]
        public void put_should_reject_authority_that_cannot_be_discovered()
        {
            Mocker.GetMock<IOidcDiscoveryService>()
                  .Setup(s => s.IsDiscoverable(It.IsAny<string>()))
                  .Returns(false);

            var failures = Subject.Validator.Validate(OidcResource()).Errors;

            failures.Should().Contain(e => e.PropertyName == nameof(HostConfigResource.OidcAuthority));
        }

        [Test]
        public void put_should_not_rediscover_unchanged_authority()
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.OidcAuthority).Returns("https://idp.example.com");

            Subject.Validator.Validate(OidcResource());

            Mocker.GetMock<IOidcDiscoveryService>().Verify(s => s.IsDiscoverable(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_require_oidc_fields_only_for_oidc()
        {
            var resource = new HostConfigResource { AuthenticationMethod = AuthenticationType.None };

            Subject.Validator.Validate(resource).Errors
                   .Where(e => e.PropertyName.StartsWith("Oidc"))
                   .Should().BeEmpty();
        }

        [Test]
        public void should_require_oidc_fields_for_oidc()
        {
            var resource = new HostConfigResource { AuthenticationMethod = AuthenticationType.Oidc };

            var names = Subject.Validator.Validate(resource).Errors.Select(e => e.PropertyName).ToList();

            names.Should().Contain(new[]
            {
                nameof(HostConfigResource.OidcAuthority),
                nameof(HostConfigResource.OidcClientId),
                nameof(HostConfigResource.OidcClientSecret),
                nameof(HostConfigResource.OidcUserIdentifier),
                nameof(HostConfigResource.OidcScopes)
            });
        }
    }
}
