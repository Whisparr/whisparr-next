using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using Whisparr.Http.Authentication;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class OidcOptionsChangeTokenSourceFixture
    {
        private string _authority;
        private string _secret;
        private string _urlBase;
        private AuthenticationType _method;
        private int _fired;
        private OidcOptionsChangeTokenSource _subject;

        [SetUp]
        public void Setup()
        {
            _authority = "https://idp.example.com";
            _secret = "s3cret";
            _urlBase = string.Empty;
            _method = AuthenticationType.Oidc;
            _fired = 0;

            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.AuthenticationMethod).Returns(() => _method);
            config.SetupGet(c => c.OidcAuthority).Returns(() => _authority);
            config.SetupGet(c => c.OidcClientId).Returns("whisparr");
            config.SetupGet(c => c.OidcClientSecret).Returns(() => _secret);
            config.SetupGet(c => c.OidcUserIdentifier).Returns("user@example.com");
            config.SetupGet(c => c.OidcScopes).Returns("openid");
            config.SetupGet(c => c.UrlBase).Returns(() => _urlBase);

            _subject = new OidcOptionsChangeTokenSource(config.Object);

            Watch();
        }

        private void Watch()
        {
            _subject.GetChangeToken().RegisterChangeCallback(_ => _fired++, null);
        }

        [Test]
        public void should_be_named_for_the_oidc_scheme()
        {
            _subject.Name.Should().Be("Oidc");
        }

        [Test]
        public void should_not_fire_when_nothing_relevant_changed()
        {
            _subject.Handle(new ConfigFileSavedEvent());

            _fired.Should().Be(0);
        }

        [Test]
        public void should_fire_when_authority_changed()
        {
            _authority = "https://other.example.com";

            _subject.Handle(new ConfigFileSavedEvent());

            _fired.Should().Be(1);
        }

        [Test]
        public void should_fire_when_secret_changed()
        {
            _secret = "new";

            _subject.Handle(new ConfigFileSavedEvent());

            _fired.Should().Be(1);
        }

        [Test]
        public void should_fire_when_method_or_url_base_changed()
        {
            _method = AuthenticationType.Forms;
            _subject.Handle(new ConfigFileSavedEvent());

            _fired.Should().Be(1);

            Watch();
            _urlBase = "/whisparr";
            _subject.Handle(new ConfigFileSavedEvent());

            _fired.Should().Be(2);
        }

        [Test]
        public void should_hand_out_a_fresh_token_after_a_change_and_not_refire_for_the_same_settings()
        {
            var before = _subject.GetChangeToken();

            _authority = "https://other.example.com";
            _subject.Handle(new ConfigFileSavedEvent());

            _subject.GetChangeToken().Should().NotBeSameAs(before);

            Watch();
            _subject.Handle(new ConfigFileSavedEvent());

            _fired.Should().Be(1);
        }
    }
}
