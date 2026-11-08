using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Jav;
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

        // A release title the parser gave no Code, so it is neither a scene nor a movie by its own flags
        private static ParsedMovieInfo CodelessParse() => new() { ReleaseTitle = "ABP123.720p", MovieTitles = new List<string> { "ABP123" } };

        [Test]
        public void file_import_codeless_unhyphenated_release_finds_jav()
        {
            var jav = new Movie { Id = 6 };
            var parsed = CodelessParse();

            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            Mocker.GetMock<IMovieService>().Setup(s => s.FindScene(parsed, false, null)).Returns(jav);

            Subject.Map(parsed, "", 0).Movie.Should().Be(jav);
        }

        [Test]
        public void file_import_unhyphenated_code_finds_jav()
        {
            var jav = new Movie { Id = 5 };
            var parsed = Parser.Parser.ParseMovieTitle("abp123.mp4");

            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            Mocker.GetMock<IMovieService>().Setup(s => s.FindScene(parsed, false, null)).Returns(jav);

            Subject.Map(parsed, "", 0).Movie.Should().Be(jav);
        }

        [Test]
        public void file_import_unhyphenated_code_miss_falls_through_to_movie_lookup()
        {
            var parsed = CodelessParse();

            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);

            Subject.Map(parsed, "", 0).Movie.Should().BeNull();
            Mocker.GetMock<IMovieService>().Verify(s => s.FindScene(parsed, false, null), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(s => s.FindByTitle(It.IsAny<List<string>>(), It.IsAny<int?>(), It.IsAny<List<string>>(), It.IsAny<List<Movie>>()), Times.AtLeastOnce());
        }

        [Test]
        public void file_import_unhyphenated_code_skips_jav_when_unavailable()
        {
            var parsed = CodelessParse();

            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(false);

            Subject.Map(parsed, "", 0);

            Mocker.GetMock<IMovieService>().Verify(s => s.FindScene(It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<SearchCriteriaBase>()), Times.Never());
        }

        [Test]
        public void western_scene_with_date_still_takes_scene_path_when_jav_available()
        {
            var parsed = Parser.Parser.ParseMovieTitle("Brazzers.Exxtra.20.01.02.Some.Title.1080p.mp4");
            var scene = new Movie { Id = 9 };

            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            Mocker.GetMock<IMovieService>().Setup(s => s.FindScene(parsed, false, null)).Returns(scene);

            Subject.Map(parsed, "", 0).Movie.Should().Be(scene);
            Mocker.GetMock<IMovieService>().Verify(s => s.FindScene(parsed, false, null), Times.Once());
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
