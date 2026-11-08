using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class JavSearchMapFixture : TestBase<ParsingService>
    {
        private MovieSearchCriteria _javCriteria;

        [SetUp]
        public void Setup()
        {
            _javCriteria = new MovieSearchCriteria
            {
                Movie = new Movie { Id = 1, MovieMetadata = new MovieMetadata { ItemType = ItemType.Jav, Code = "ABP-123" } }
            };
        }

        private void VerifyNoTitleOrIdLookups()
        {
            var service = Mocker.GetMock<IMovieService>();

            service.Verify(s => s.FindByTitle(It.IsAny<string>()), Times.Never());
            service.Verify(s => s.FindByTitle(It.IsAny<List<string>>(), It.IsAny<int?>(), It.IsAny<List<string>>(), It.IsAny<List<Movie>>()), Times.Never());
            service.Verify(s => s.FindByTitleCandidates(It.IsAny<List<string>>(), out It.Ref<List<string>>.IsAny), Times.Never());
            service.Verify(s => s.FindByTmdbId(It.IsAny<int>()), Times.Never());
            service.Verify(s => s.FindFuzzyMovieByYear(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void jav_search_code_miss_returns_null_without_title_or_id_lookups()
        {
            var parsed = new ParsedMovieInfo
            {
                Code = "ABP-999",
                MovieTitles = new List<string> { "Perfect Satisfaction" },
                Year = 2020
            };

            var result = Subject.Map(parsed, "", 123, _javCriteria);

            result.Movie.Should().BeNull();
            Mocker.GetMock<IMovieService>().Verify(s => s.FindScene(parsed, false, _javCriteria), Times.Once());
            VerifyNoTitleOrIdLookups();
        }

        [Test]
        public void jav_search_dateless_scene_shaped_parse_does_not_fall_back_to_movie_lookup()
        {
            var parsed = new ParsedMovieInfo
            {
                IsDatelessScene = true,
                StudioTitle = "Studio",
                MovieTitles = new List<string> { "Studio - Title" },
                Year = 2020
            };

            var result = Subject.Map(parsed, "", 123, _javCriteria);

            result.Movie.Should().BeNull();
            VerifyNoTitleOrIdLookups();
        }
    }
}
