using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class OidcDiscoveryServiceFixture : CoreTest<OidcDiscoveryService>
    {
        private const string ValidBody = "{\"authorization_endpoint\":\"https://id.example/authorize\",\"token_endpoint\":\"https://id.example/token\"}";

        private HttpRequest _request;

        private void GivenResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<OidcDiscoveryDocument>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(request =>
                  {
                      _request = request;

                      return new HttpResponse<OidcDiscoveryDocument>(new HttpResponse(request, new HttpHeader(), body, statusCode));
                  });
        }

        [Test]
        public void should_request_well_known_configuration()
        {
            GivenResponse(ValidBody);

            Subject.IsDiscoverable("https://id.example");

            _request.Url.FullUri.Should().EndWith("/.well-known/openid-configuration");
        }

        [Test]
        public void should_be_discoverable_with_both_endpoints()
        {
            GivenResponse(ValidBody);

            Subject.IsDiscoverable("https://id.example").Should().BeTrue();
        }

        [Test]
        public void should_not_be_discoverable_on_http_error()
        {
            GivenResponse(ValidBody, HttpStatusCode.NotFound);

            Subject.IsDiscoverable("https://id.example").Should().BeFalse();
        }

        [Test]
        public void should_not_be_discoverable_without_token_endpoint()
        {
            GivenResponse("{\"authorization_endpoint\":\"https://id.example/authorize\"}");

            Subject.IsDiscoverable("https://id.example").Should().BeFalse();
        }

        [Test]
        public void should_not_be_discoverable_when_request_throws()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<OidcDiscoveryDocument>(It.IsAny<HttpRequest>()))
                  .Throws(new InvalidOperationException("boom"));

            Subject.IsDiscoverable("https://id.example").Should().BeFalse();
        }

        [Test]
        public void should_not_be_discoverable_for_blank_authority()
        {
            Subject.IsDiscoverable(" ").Should().BeFalse();

            Mocker.GetMock<IHttpClient>()
                  .Verify(c => c.Get<OidcDiscoveryDocument>(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
