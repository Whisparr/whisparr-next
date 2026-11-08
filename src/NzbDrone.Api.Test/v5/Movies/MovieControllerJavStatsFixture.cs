using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.MovieStats;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Movies;

namespace NzbDrone.Api.Test.v5.Movies
{
    [TestFixture]
    public class MovieControllerJavStatsFixture : TestBase<MovieController>
    {
        [TestCase("jav", ItemType.Jav)]
        [TestCase("JAV", ItemType.Jav)]
        [TestCase("scene", ItemType.Scene)]
        [TestCase("movie", ItemType.Movie)]
        [TestCase(null, ItemType.Movie)]
        [TestCase("bogus", ItemType.Movie)]
        public void stats_itemtype_jav_counts_jav_only(string itemType, ItemType expected)
        {
            Mocker.GetMock<IMovieStatisticsService>()
                  .Setup(s => s.GetMovieIndexOverview(It.IsAny<ItemType>()))
                  .Returns(new MovieIndexOverview());

            Subject.GetStats(itemType);

            Mocker.GetMock<IMovieStatisticsService>()
                  .Verify(s => s.GetMovieIndexOverview(expected), Times.Once());
            Mocker.GetMock<IMovieStatisticsService>()
                  .Verify(s => s.GetMovieIndexOverview(It.Is<ItemType>(t => t != expected)), Times.Never());
        }
    }
}
