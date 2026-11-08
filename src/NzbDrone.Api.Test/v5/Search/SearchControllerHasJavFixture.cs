using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Search;

namespace NzbDrone.Api.Test.v5.Search
{
    [TestFixture]
    public class SearchControllerHasJavFixture : TestBase<SearchController>
    {
        private void GivenAvailable(bool available)
        {
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(available);
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(null, false)]
        public void studio_has_jav_reports_skyhook_answer(bool? skyhook, bool expected)
        {
            GivenAvailable(true);
            Mocker.GetMock<IProvideMovieInfo>().Setup(s => s.GetStudioHasJav("site-uuid")).Returns(skyhook);

            Subject.StudioHasJav("site-uuid").HasJav.Should().Be(expected);
        }

        [TestCase(true, true)]
        [TestCase(null, false)]
        public void performer_has_jav_reports_skyhook_answer(bool? skyhook, bool expected)
        {
            GivenAvailable(true);
            Mocker.GetMock<IProvideMovieInfo>().Setup(s => s.GetPerformerHasJav("performer-uuid")).Returns(skyhook);

            Subject.PerformerHasJav("performer-uuid").HasJav.Should().Be(expected);
        }

        [Test]
        public void unavailable_never_asks_skyhook()
        {
            GivenAvailable(false);

            Subject.StudioHasJav("site-uuid").HasJav.Should().BeFalse();
            Subject.PerformerHasJav("performer-uuid").HasJav.Should().BeFalse();

            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.GetStudioHasJav(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.GetPerformerHasJav(It.IsAny<string>()), Times.Never());
        }
    }
}
