using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.LibrarySearch;
using NzbDrone.Core.Movies;
using NzbDrone.Core.MovieStats;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.LibrarySearch;

namespace NzbDrone.Api.Test.v5.LibrarySearch
{
    [TestFixture]
    public class LibrarySearchControllerJavFixture : TestBase<LibrarySearchController>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMovieStatisticsService>()
                  .Setup(s => s.MovieStatistics(It.IsAny<List<int>>()))
                  .Returns(new List<MovieStatistics>());
        }

        [Test]
        public void library_search_returns_jav_bucket()
        {
            var jav = new Movie { Id = 9, MovieMetadata = new MovieMetadata { Title = "J", ItemType = ItemType.Jav } };

            Mocker.GetMock<ILibrarySearchService>()
                  .Setup(s => s.Search("j", 4))
                  .Returns(new LibrarySearchResult
                  {
                      Jav = new LibrarySearchPage<Movie> { TotalRecords = 1, Records = new List<Movie> { jav } }
                  });

            var result = Subject.Search("j");

            result.Jav.TotalRecords.Should().Be(1);
            result.Jav.Records.Should().ContainSingle().Which.Id.Should().Be(9);
            result.Scenes.Records.Should().BeEmpty();
        }

        [Test]
        public void library_search_jav_route_searches_the_jav_item_type()
        {
            Mocker.GetMock<ILibrarySearchService>()
                  .Setup(s => s.SearchMovies("j", ItemType.Jav, 1, 20))
                  .Returns(new LibrarySearchPage<Movie>());

            Subject.SearchJav("j");

            Mocker.GetMock<ILibrarySearchService>()
                  .Verify(s => s.SearchMovies("j", ItemType.Jav, 1, 20), Times.Once());
        }
    }
}
