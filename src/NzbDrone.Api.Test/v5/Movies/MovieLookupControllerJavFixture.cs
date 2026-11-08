using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Movies;

namespace NzbDrone.Api.Test.v5.Movies
{
    [TestFixture]
    public class MovieLookupControllerJavFixture : TestBase<MovieLookupController>
    {
        [Test]
        public void jav_lookup_is_empty_and_never_hits_skyhook_when_unavailable()
        {
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(false);

            Subject.Search("ABP-123", ItemType.Jav).Should().BeEmpty();

            Mocker.GetMock<ISearchForNewMovie>().Verify(s => s.SearchForNewEntity(It.IsAny<string>(), It.IsAny<ItemType?>()), Times.Never());
        }

        [Test]
        public void jav_lookup_searches_when_available()
        {
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(true);
            Mocker.GetMock<ISearchForNewMovie>().Setup(s => s.SearchForNewEntity("ABP-123", ItemType.Jav)).Returns(new List<object>());

            Subject.Search("ABP-123", ItemType.Jav).ToList();

            Mocker.GetMock<ISearchForNewMovie>().Verify(s => s.SearchForNewEntity("ABP-123", ItemType.Jav), Times.Once());
        }
    }
}
