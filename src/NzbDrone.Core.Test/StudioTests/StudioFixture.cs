using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.StudioTests
{
    [TestFixture]
    public class StudioFixture : CoreTest
    {
        [Test]
        public void should_default_new_studios_to_visible()
        {
            new Studio().Visibility.Should().Be(StudioVisibility.Visible);
        }

        [Test]
        public void should_default_new_studios_to_download_covers()
        {
            new Studio().DownloadCovers.Should().BeTrue();
        }

        [Test]
        public void should_unmonitor_a_studio_when_hiding_it()
        {
            var studio = GivenStudio(StudioVisibility.Visible, true, true);

            studio.ApplyVisibility(StudioVisibility.Hidden);

            studio.Visibility.Should().Be(StudioVisibility.Hidden);
            studio.Monitored.Should().BeFalse();
            studio.MoviesMonitored.Should().BeFalse();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void should_show_a_hidden_studio_once_monitored(bool monitored, bool moviesMonitored)
        {
            var studio = GivenStudio(StudioVisibility.Hidden, monitored, moviesMonitored);

            studio.ApplyVisibility(StudioVisibility.Hidden);

            studio.Visibility.Should().Be(StudioVisibility.Visible);
            studio.Monitored.Should().Be(monitored);
            studio.MoviesMonitored.Should().Be(moviesMonitored);
        }

        [Test]
        public void should_keep_an_unmonitored_hidden_studio_hidden()
        {
            var studio = GivenStudio(StudioVisibility.Hidden);

            studio.ApplyVisibility(StudioVisibility.Hidden);

            studio.Visibility.Should().Be(StudioVisibility.Hidden);
        }

        [Test]
        public void should_show_a_hidden_studio_on_request()
        {
            var studio = GivenStudio(StudioVisibility.Hidden);

            studio.ApplyVisibility(StudioVisibility.Visible);

            studio.Visibility.Should().Be(StudioVisibility.Visible);
        }

        [Test]
        public void should_keep_an_unmonitored_visible_studio_visible()
        {
            var studio = GivenStudio(StudioVisibility.Visible);

            studio.ApplyVisibility(StudioVisibility.Visible);

            studio.Visibility.Should().Be(StudioVisibility.Visible);
        }

        private static Studio GivenStudio(StudioVisibility visibility, bool monitored = false, bool moviesMonitored = false)
        {
            return new Studio
            {
                Visibility = visibility,
                Monitored = monitored,
                MoviesMonitored = moviesMonitored
            };
        }
    }
}
