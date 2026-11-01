using System.Net.Security;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Security
{
    [TestFixture]
    public class X509CertificateValidationServiceFixture : CoreTest<X509CertificateValidationService>
    {
        // IP literals, so the rules are tested without any DNS lookup
        private const string RemoteHost = "93.184.216.34";
        private const string LocalHost = "192.168.1.10";
        private const SslPolicyErrors ExpiredCertificate = SslPolicyErrors.RemoteCertificateChainErrors;

        private void GivenValidation(CertificateValidationType type)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.CertificateValidation).Returns(type);
        }

        private bool Check(string host, SslPolicyErrors errors = ExpiredCertificate)
        {
            return Subject.ShouldByPassValidationError(host, null, null, errors);
        }

        [TestCase(CertificateValidationType.Enabled)]
        [TestCase(CertificateValidationType.DisabledForLocalAddresses)]
        public void should_reject_a_bad_certificate_on_a_remote_address(CertificateValidationType type)
        {
            GivenValidation(type);

            Check(RemoteHost).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_reject_a_bad_certificate_on_a_local_address_when_enabled()
        {
            GivenValidation(CertificateValidationType.Enabled);

            Check(LocalHost).Should().BeFalse();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_accept_a_bad_certificate_on_a_local_address_when_disabled_for_local()
        {
            GivenValidation(CertificateValidationType.DisabledForLocalAddresses);

            Check(LocalHost).Should().BeTrue();
        }

        [Test]
        public void should_accept_a_bad_certificate_on_a_remote_address_when_disabled()
        {
            GivenValidation(CertificateValidationType.Disabled);

            Check(RemoteHost).Should().BeTrue();
        }

        [TestCase("localhost")]
        [TestCase("127.0.0.1")]
        public void should_always_accept_this_machine(string host)
        {
            GivenValidation(CertificateValidationType.Enabled);

            Check(host).Should().BeTrue();
        }

        [Test]
        public void should_accept_a_good_certificate_when_enabled()
        {
            GivenValidation(CertificateValidationType.Enabled);

            Check(RemoteHost, SslPolicyErrors.None).Should().BeTrue();
        }
    }
}
