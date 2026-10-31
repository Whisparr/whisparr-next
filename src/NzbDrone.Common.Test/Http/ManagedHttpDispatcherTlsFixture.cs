using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http
{
    // An expired certificate served from this machine, so the dispatcher's handling of the validation
    // decision doesn't depend on any outside site. The decision itself is X509CertificateValidationServiceFixture's.
    [TestFixture]
    public class ManagedHttpDispatcherTlsFixture : TestBase<ManagedHttpDispatcher>
    {
        private TcpListener _listener;
        private X509Certificate2 _certificate;
        private CancellationTokenSource _stop;
        private Task _server;

        [SetUp]
        public void Setup()
        {
            _certificate = CreateExpiredCertificate();
            _stop = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _server = Task.Run(() => Serve(_stop.Token));

            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.GetMock<IUserAgentBuilder>().Setup(b => b.GetUserAgent(It.IsAny<bool>())).Returns("Whisparr-Test");
        }

        [TearDown]
        public async Task TearDown()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _server;

            _stop.Dispose();
            _certificate.Dispose();
        }

        private static X509Certificate2 CreateExpiredCertificate()
        {
            using var key = RSA.Create(2048);

            var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var expired = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

            return X509CertificateLoader.LoadPkcs12(expired.Export(X509ContentType.Pfx), null);
        }

        private async Task Serve(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(token);
                    await Respond(client, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException or ObjectDisposedException)
                {
                    // The client rejecting the certificate ends the handshake here, which is the point of one test
                }
            }
        }

        private async Task Respond(TcpClient client, CancellationToken token)
        {
            await using var stream = new SslStream(client.GetStream());
            await stream.AuthenticateAsServerAsync(_certificate);

            var buffer = new byte[4096];
            var request = new StringBuilder();

            while (!request.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer, token);

                if (read == 0)
                {
                    return;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"), token);
        }

        private HttpRequest Request()
        {
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            return new HttpRequest($"https://127.0.0.1:{port}/");
        }

        private void GivenValidationDecision(bool bypass)
        {
            Mocker.GetMock<ICertificateValidationService>()
                  .Setup(s => s.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.IsAny<SslPolicyErrors>()))
                  .Returns(bypass);
        }

        [Test]
        public void should_fail_the_request_when_validation_rejects_the_certificate()
        {
            GivenValidationDecision(false);

            Assert.ThrowsAsync<HttpRequestException>(async () => await Subject.GetResponseAsync(Request(), new CookieContainer()));
        }

        [Test]
        public async Task should_complete_the_request_when_validation_accepts_the_certificate()
        {
            GivenValidationDecision(true);

            var response = await Subject.GetResponseAsync(Request(), new CookieContainer());

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Should().Be("ok");
        }

        [Test]
        public async Task should_ask_validation_about_the_expired_certificate()
        {
            GivenValidationDecision(true);

            await Subject.GetResponseAsync(Request(), new CookieContainer());

            Mocker.GetMock<ICertificateValidationService>()
                  .Verify(s => s.ShouldByPassValidationError(It.IsAny<object>(), It.IsAny<X509Certificate>(), It.IsAny<X509Chain>(), It.Is<SslPolicyErrors>(e => e != SslPolicyErrors.None)), Times.AtLeastOnce());
        }
    }
}
