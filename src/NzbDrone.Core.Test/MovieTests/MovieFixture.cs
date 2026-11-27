using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieFixture : CoreTest
    {
        [Test]
        public void should_default_new_movies_to_visible()
        {
            new Movie().Visibility.Should().Be(MovieVisibility.Visible);
        }

        [Test]
        public void should_default_new_movies_to_download_covers()
        {
            new Movie().DownloadCovers.Should().BeTrue();
        }

        [Test]
        public void should_unmonitor_a_movie_when_hiding_it()
        {
            var movie = GivenMovie(MovieVisibility.Visible, true);

            movie.ApplyVisibility(MovieVisibility.Hidden);

            movie.Visibility.Should().Be(MovieVisibility.Hidden);
            movie.Monitored.Should().BeFalse();
        }

        [Test]
        public void should_show_a_hidden_movie_once_monitored()
        {
            var movie = GivenMovie(MovieVisibility.Hidden, true);

            movie.ApplyVisibility(MovieVisibility.Hidden);

            movie.Visibility.Should().Be(MovieVisibility.Visible);
            movie.Monitored.Should().BeTrue();
        }

        [Test]
        public void should_keep_an_unmonitored_hidden_movie_hidden()
        {
            var movie = GivenMovie(MovieVisibility.Hidden);

            movie.ApplyVisibility(MovieVisibility.Hidden);

            movie.Visibility.Should().Be(MovieVisibility.Hidden);
        }

        [Test]
        public void should_show_a_hidden_movie_on_request()
        {
            var movie = GivenMovie(MovieVisibility.Hidden);

            movie.ApplyVisibility(MovieVisibility.Visible);

            movie.Visibility.Should().Be(MovieVisibility.Visible);
        }

        [Test]
        public void should_keep_an_unmonitored_visible_movie_visible()
        {
            var movie = GivenMovie(MovieVisibility.Visible);

            movie.ApplyVisibility(MovieVisibility.Visible);

            movie.Visibility.Should().Be(MovieVisibility.Visible);
        }

        private static Movie GivenMovie(MovieVisibility visibility, bool monitored = false)
        {
            return new Movie
            {
                Visibility = visibility,
                Monitored = monitored
            };
        }
    }
}
