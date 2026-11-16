using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;

namespace NzbDrone.Core.Test.MovieTests.MovieServiceTests
{
    [TestFixture]
    public class MatchOnStudioDateFixture
    {
        private Mock<IMovieRepository> _movieRepositoryMock;
        private Mock<IConfigService> _configServiceMock;
        private Mock<ICreditService> _creditServiceMock;
        private MovieService _movieService;

        [SetUp]
        public void Setup()
        {
            _movieRepositoryMock = new Mock<IMovieRepository>();
            _configServiceMock = new Mock<IConfigService>();
            _creditServiceMock = new Mock<ICreditService>();

            // Correct constructor order: IMovieRepository, IStudioService, IEventAggregator, IConfigService, IBuildMoviePaths, IAutoTaggingService, ICacheManager, Logger
            _movieService = new MovieService(
                _movieRepositoryMock.Object,
                _creditServiceMock.Object,
                null, // IStudioService
                null, // IEventAggregator
                _configServiceMock.Object,
                null, // IBuildMoviePaths
                null, // IAutoTaggingService
                null, // ICacheManager
                null, // IJavAvailability
                NzbDrone.Common.Instrumentation.NzbDroneLogger.GetLogger(typeof(MovieService))); // Logger
        }

        [Test]
        public void Should_return_movie_by_studio_and_date_when_setting_enabled_and_title_parse_fails()
        {
            var studioId = "studio-1";
            var releaseDate = "2023-01-01";
            var movie = new Movie { Id = 1 };
            movie.MovieMetadata.Value.StudioForeignId = studioId;
            movie.MovieMetadata.Value.ReleaseDate = releaseDate;

            _movieRepositoryMock.Setup(r => r.FindByStudioAndDate(studioId, releaseDate))
                .Returns(new List<Movie> { movie });
            _configServiceMock.SetupGet(c => c.WhisparrAutoMatchOnDate).Returns(true);

            var result = _movieService.InvokeFindByStudioAndReleaseDate(studioId, releaseDate, "", "", "");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo(movie.Id));
        }

        [Test]
        public void Should_not_return_movie_by_studio_and_date_when_setting_disabled_and_title_parse_fails()
        {
            var studioId = "studio-1";
            var releaseDate = "2023-01-01";
            var movie = new Movie { Id = 1 };
            movie.MovieMetadata.Value.StudioForeignId = studioId;
            movie.MovieMetadata.Value.ReleaseDate = releaseDate;

            _movieRepositoryMock.Setup(r => r.FindByStudioAndDate(studioId, releaseDate))
                .Returns(new List<Movie> { movie });
            _configServiceMock.SetupGet(c => c.WhisparrAutoMatchOnDate).Returns(false);

            var result = _movieService.InvokeFindByStudioAndReleaseDate(studioId, releaseDate, "", "", "");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Should_not_match_release_with_different_date_via_studio_fallback_when_WhisparrAutoMatchOnDate_enabled()
        {
            // Regression: searching for a movie by date returns releases from the same studio with
            // different dates. The studio has only one library movie, so GetByStudioForeignId returns
            // a single result. WhisparrAutoMatchOnDate must NOT fire here because the date query
            // itself found nothing — we fell back to the studio-only query (verifyDate=true).
            var studioId = "studio-1";
            var libraryMovieDate = "2022-11-16";
            var releaseDateNotInLibrary = "2022-11-30";

            var libraryMovie = new Movie { Id = 1 };
            libraryMovie.MovieMetadata.Value.StudioForeignId = studioId;
            libraryMovie.MovieMetadata.Value.ReleaseDate = libraryMovieDate;

            _movieRepositoryMock.Setup(r => r.FindByStudioAndDate(studioId, releaseDateNotInLibrary))
                .Returns(new List<Movie>());
            _movieRepositoryMock.Setup(r => r.GetByStudioForeignId(studioId))
                .Returns(new List<Movie> { libraryMovie });
            _configServiceMock.SetupGet(c => c.WhisparrAutoMatchOnDate).Returns(true);

            var result = _movieService.InvokeFindByStudioAndReleaseDate(studioId, releaseDateNotInLibrary, "", "", "");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Should_still_match_when_date_query_finds_exactly_one_movie_and_WhisparrAutoMatchOnDate_enabled()
        {
            // The date query itself returns the one matching movie — WhisparrAutoMatchOnDate
            // should still fire in this case (verifyDate=false, genuine date match).
            var studioId = "studio-1";
            var releaseDate = "2022-11-16";

            var movie = new Movie { Id = 1 };
            movie.MovieMetadata.Value.StudioForeignId = studioId;
            movie.MovieMetadata.Value.ReleaseDate = releaseDate;

            _movieRepositoryMock.Setup(r => r.FindByStudioAndDate(studioId, releaseDate))
                .Returns(new List<Movie> { movie });
            _configServiceMock.SetupGet(c => c.WhisparrAutoMatchOnDate).Returns(true);

            var result = _movieService.InvokeFindByStudioAndReleaseDate(studioId, releaseDate, "", "", "");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo(movie.Id));
        }

        [Test]
        public void Should_not_match_titles_against_studio_catalogue_without_a_scene_on_the_release_date()
        {
            // A new scene isn't in the library yet, so the studio fallback can't find anything released that day.
            var studioId = "studio-1";
            var other = new Movie { Id = 1 };
            other.MovieMetadata.Value.Title = "Some Other Scene";
            other.MovieMetadata.Value.StudioForeignId = studioId;
            other.MovieMetadata.Value.ReleaseDateUtc = new DateTime(2026, 11, 7);

            _movieRepositoryMock.Setup(r => r.FindByStudioAndDate(studioId, It.IsAny<string>())).Returns(new List<Movie>());
            _movieRepositoryMock.Setup(r => r.StudioHasSceneReleasedOn(studioId, new DateTime(2026, 11, 14))).Returns(false);
            _movieRepositoryMock.Setup(r => r.GetByStudioForeignId(studioId)).Returns(new List<Movie> { other });

            var result = _movieService.InvokeFindByStudioAndReleaseDate(studioId, "2026-11-14", "alyssa pure red bikini", "", "");

            Assert.That(result, Is.Null);
            _movieRepositoryMock.Verify(r => r.GetByStudioForeignId(It.IsAny<string>()), Times.Never);
            _creditServiceMock.Verify(c => c.GetAllCreditsForMovieMetadata(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void Should_not_match_titles_against_studio_catalogue_when_the_release_date_is_partial()
        {
            var studioId = "studio-1";

            _movieRepositoryMock.Setup(r => r.FindByStudioAndDate(studioId, It.IsAny<string>())).Returns(new List<Movie>());

            var result = _movieService.InvokeFindByStudioAndReleaseDate(studioId, "2026-11", "red bikini", "", "");

            Assert.That(result, Is.Null);
            _movieRepositoryMock.Verify(r => r.StudioHasSceneReleasedOn(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
            _movieRepositoryMock.Verify(r => r.GetByStudioForeignId(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Should_match_title_from_studio_catalogue_when_a_scene_was_released_on_the_release_date()
        {
            // The date query matches the stored date text; the fallback still finds a scene whose release date is that day.
            var studioId = "studio-1";
            var movie = new Movie { Id = 1 };
            movie.MovieMetadata.Value.Title = "Red Bikini";
            movie.MovieMetadata.Value.StudioForeignId = studioId;
            movie.MovieMetadata.Value.ReleaseDateUtc = new DateTime(2026, 11, 14);

            _movieRepositoryMock.Setup(r => r.FindByStudioAndDate(studioId, It.IsAny<string>())).Returns(new List<Movie>());
            _movieRepositoryMock.Setup(r => r.StudioHasSceneReleasedOn(studioId, new DateTime(2026, 11, 14))).Returns(true);
            _movieRepositoryMock.Setup(r => r.GetByStudioForeignId(studioId)).Returns(new List<Movie> { movie });

            var result = _movieService.InvokeFindByStudioAndReleaseDate(studioId, "2026-11-14", "red bikini", "", "");

            Assert.That(result, Is.SameAs(movie));
        }
    }

    public static class MovieServiceTestExtensions
    {
        public static Movie InvokeFindByStudioAndReleaseDate(this MovieService service, string studioId, string releaseDate, string releaseTokens, string foreignId, string episode, bool interactiveSearch = false)
        {
            var method = typeof(MovieService).GetMethod("FindByStudioAndReleaseDate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (Movie)method.Invoke(service, new object[] { studioId, releaseDate, releaseTokens, foreignId, episode, interactiveSearch, false });
        }
    }
}
