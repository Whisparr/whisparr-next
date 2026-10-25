using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class SkyhookPreflightFixture : CoreTest<SkyhookPreflight>
    {
        private HttpRequest _sent;

        [SetUp]
        public void Setup()
        {
            var builder = new Mock<IWhisparrCloudRequestBuilder>();
            builder.SetupGet(b => b.WhisparrMetadata).Returns(new HttpRequestBuilder("https://skyhook.test/v5/{route}").CreateFactory());
            Mocker.SetConstant(builder.Object);
        }

        private void Respond(HttpStatusCode status)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _sent = r)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "[]", status));
        }

        [Test]
        public void preflight_should_pass_on_200()
        {
            Respond(HttpStatusCode.OK);

            Subject.Invoking(s => s.Check()).Should().NotThrow();

            _sent.Url.FullUri.Should().Contain("/v5/movie/changed").And.Contain("since=");
            _sent.RequestTimeout.Should().Be(TimeSpan.FromSeconds(15));
            _sent.SuppressHttpError.Should().BeTrue();
        }

        [Test]
        public void preflight_should_throw_on_503()
        {
            Respond(HttpStatusCode.ServiceUnavailable);

            Subject.Invoking(s => s.Check()).Should().Throw<SetupStepException>()
                   .Which.UserMessage.Should().Contain("Skyhook didn't answer").And.Contain("503");
        }

        [Test]
        public void preflight_should_throw_on_timeout()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("Http request timed out", WebExceptionStatus.Timeout));

            Subject.Invoking(s => s.Check()).Should().Throw<SetupStepException>()
                   .Which.UserMessage.Should().Contain("Skyhook didn't answer").And.Contain("timed out");
        }
    }
}
