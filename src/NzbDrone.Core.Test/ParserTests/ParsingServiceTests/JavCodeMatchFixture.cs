using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class JavCodeMatchFixture : CoreTest<MovieService>
    {
        private Movie _jav;

        [SetUp]
        public void Setup()
        {
            _jav = new Movie { Id = 1, MovieMetadata = new MovieMetadata { ItemType = ItemType.Jav, Code = "ABP-123" } };

            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            Mocker.GetMock<IMovieRepository>().Setup(r => r.FindByJavCode("ABP-123")).Returns(_jav);
            Mocker.GetMock<IMovieRepository>().Setup(r => r.FindByTitles(It.IsAny<List<string>>())).Returns(new List<Movie>());
        }

        private static ParsedMovieInfo Parsed(string code) => new() { Code = code, MovieTitles = new List<string> { "x" } };

        private static MovieSearchCriteria Criteria(ItemType type) => new()
        {
            Movie = new Movie { MovieMetadata = new MovieMetadata { ItemType = type } }
        };

        [Test]
        public void code_only_file_matches_jav()
        {
            Subject.FindScene(Parsed("ABP-123")).Should().Be(_jav);
        }

        [Test]
        public void lowercase_nodash_code_matches()
        {
            Subject.FindScene(Parsed("abp123")).Should().Be(_jav);
        }

        [Test]
        public void unhyphenated_file_matches_jav()
        {
            var parsed = Parser.Parser.ParseMovieTitle("abp123.mp4");

            parsed.Should().NotBeNull();
            Subject.FindScene(parsed).Should().Be(_jav);
        }

        [Test]
        public void unhyphenated_release_in_jav_search_matches()
        {
            var parsed = Parser.Parser.ParseMovieTitle("ABP123.720p");

            parsed.Should().NotBeNull();
            Subject.FindScene(parsed, false, Criteria(ItemType.Jav)).Should().Be(_jav);
        }

        [Test]
        public void jav_search_miss_does_not_title_match_scene()
        {
            Subject.FindScene(Parsed("ABP-999"), false, Criteria(ItemType.Jav)).Should().BeNull();

            Mocker.GetMock<IMovieRepository>().Verify(r => r.FindByTitles(It.IsAny<List<string>>()), Times.Never());
        }

        [Test]
        public void jav_search_hit_returns_movie()
        {
            Subject.FindScene(Parsed("ABP-123"), false, Criteria(ItemType.Jav)).Should().Be(_jav);
        }

        [Test]
        public void non_jav_search_falls_through_unchanged()
        {
            Subject.FindScene(Parsed("ABP-999"), false, Criteria(ItemType.Scene)).Should().BeNull();

            Mocker.GetMock<IMovieRepository>().Verify(r => r.FindByTitles(It.IsAny<List<string>>()), Times.AtLeastOnce());
        }

        [Test]
        public void scene_search_never_maps_to_jav_row()
        {
            Subject.FindScene(Parsed("ABP-123"), false, Criteria(ItemType.Scene)).Should().BeNull();

            Mocker.GetMock<IMovieRepository>().Verify(r => r.FindByJavCode(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void unavailable_skips_code_lookup()
        {
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(false);

            Subject.FindScene(Parsed("ABP-123")).Should().BeNull();

            Mocker.GetMock<IMovieRepository>().Verify(r => r.FindByJavCode(It.IsAny<string>()), Times.Never());
        }
    }
}
