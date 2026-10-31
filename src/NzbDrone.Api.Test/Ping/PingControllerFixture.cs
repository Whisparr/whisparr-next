using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;
using Whisparr.Http.Ping;

namespace NzbDrone.Api.Test.Ping
{
    [TestFixture]
    public class PingControllerFixture : TestBase<PingController>
    {
        private Mock<ICached<IEnumerable<Config>>> _cache;

        [SetUp]
        public void Setup()
        {
            _cache = new Mock<ICached<IEnumerable<Config>>>();

            Mocker.GetMock<ICacheManager>()
                  .Setup(c => c.GetCache<IEnumerable<Config>>(It.IsAny<Type>()))
                  .Returns(_cache.Object);
        }

        private void GivenSetupMode()
        {
            Mocker.GetMock<ISetupModeState>()
                  .SetupGet(s => s.IsActive)
                  .Returns(true);
        }

        private void GivenDatabaseFails()
        {
            _cache.Setup(c => c.Get(It.IsAny<string>(), It.IsAny<Func<IEnumerable<Config>>>(), It.IsAny<TimeSpan?>()))
                  .Throws(new InvalidOperationException("no database"));
        }

        private (int? StatusCode, string Status) Ping()
        {
            var result = (ObjectResult)Subject.GetStatus().Result;

            return (result.StatusCode, ((PingResource)result.Value).Status);
        }

        [Test]
        public void should_be_ok_when_the_database_answers()
        {
            Ping().Should().Be((StatusCodes.Status200OK, "OK"));
        }

        [Test]
        public void should_be_an_error_when_the_database_fails()
        {
            GivenDatabaseFails();

            Ping().Should().Be((StatusCodes.Status500InternalServerError, "Error"));
        }

        [Test]
        public void should_be_ok_in_setup_mode_where_there_is_no_database_to_ask()
        {
            GivenSetupMode();
            GivenDatabaseFails();

            Ping().Should().Be((StatusCodes.Status200OK, "OK"));

            _cache.Verify(c => c.Get(It.IsAny<string>(), It.IsAny<Func<IEnumerable<Config>>>(), It.IsAny<TimeSpan?>()), Times.Never());
        }
    }
}
