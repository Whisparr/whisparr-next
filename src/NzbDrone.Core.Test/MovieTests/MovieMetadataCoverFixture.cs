using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieMetadataCoverFixture : CoreTest
    {
        private static MovieMetadata Given(ItemType itemType, params MediaCoverTypes[] coverTypes)
        {
            return new MovieMetadata
            {
                ItemType = itemType,
                Images = coverTypes.Select(t => new MediaCover.MediaCover(t, t.ToString())).ToList()
            };
        }

        [TestCase(ItemType.Movie)]
        [TestCase(ItemType.Scene)]
        [TestCase(ItemType.Jav)]
        public void should_return_poster_and_fanart_when_both_exist(ItemType itemType)
        {
            var metadata = Given(itemType, MediaCoverTypes.Poster, MediaCoverTypes.Fanart);

            metadata.Poster.CoverType.Should().Be(MediaCoverTypes.Poster);
            metadata.Fanart.CoverType.Should().Be(MediaCoverTypes.Fanart);
        }

        [Test]
        public void should_not_use_fanart_as_poster_for_movie()
        {
            Given(ItemType.Movie, MediaCoverTypes.Fanart).Poster.Should().BeNull();
        }

        [TestCase(ItemType.Scene)]
        [TestCase(ItemType.Jav)]
        public void should_use_fanart_as_poster_for_non_movie(ItemType itemType)
        {
            Given(itemType, MediaCoverTypes.Fanart).Poster.CoverType.Should().Be(MediaCoverTypes.Fanart);
        }

        [Test]
        public void should_prefer_screenshot_over_fanart_as_poster_for_scene()
        {
            Given(ItemType.Scene, MediaCoverTypes.Fanart, MediaCoverTypes.Screenshot).Poster.CoverType.Should().Be(MediaCoverTypes.Screenshot);
        }

        [Test]
        public void should_use_poster_as_fanart_for_jav_only()
        {
            Given(ItemType.Jav, MediaCoverTypes.Poster).Fanart.CoverType.Should().Be(MediaCoverTypes.Poster);
            Given(ItemType.Scene, MediaCoverTypes.Poster).Fanart.Should().BeNull();
            Given(ItemType.Movie, MediaCoverTypes.Poster).Fanart.Should().BeNull();
        }

        [Test]
        public void should_yield_only_own_images_when_nothing_is_missing()
        {
            var covers = Given(ItemType.Scene, MediaCoverTypes.Poster, MediaCoverTypes.Fanart).GetEffectiveCovers().ToList();

            covers.Select(c => c.CoverType).Should().BeEquivalentTo(new[] { MediaCoverTypes.Poster, MediaCoverTypes.Fanart });
        }

        [Test]
        public void should_add_poster_from_fanart_for_scene()
        {
            var covers = Given(ItemType.Scene, MediaCoverTypes.Fanart).GetEffectiveCovers().ToList();

            covers.Select(c => c.CoverType).Should().BeEquivalentTo(new[] { MediaCoverTypes.Fanart, MediaCoverTypes.Poster });
            covers.Should().OnlyContain(c => c.Source.CoverType == MediaCoverTypes.Fanart);
        }

        [Test]
        public void should_add_fanart_from_poster_for_jav()
        {
            var covers = Given(ItemType.Jav, MediaCoverTypes.Poster).GetEffectiveCovers().ToList();

            covers.Select(c => c.CoverType).Should().BeEquivalentTo(new[] { MediaCoverTypes.Poster, MediaCoverTypes.Fanart });
            covers.Should().OnlyContain(c => c.Source.CoverType == MediaCoverTypes.Poster);
        }

        [Test]
        public void should_not_add_covers_for_movie_or_scene_with_only_poster()
        {
            Given(ItemType.Movie, MediaCoverTypes.Fanart).GetEffectiveCovers().Should().HaveCount(1);
            Given(ItemType.Scene, MediaCoverTypes.Poster).GetEffectiveCovers().Should().HaveCount(1);
            new MovieMetadata { Images = new List<MediaCover.MediaCover>() }.GetEffectiveCovers().Should().BeEmpty();
        }
    }
}
