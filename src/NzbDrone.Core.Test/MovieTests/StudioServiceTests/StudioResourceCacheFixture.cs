using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.StudioServiceTests
{
    [TestFixture]
    public class StudioResourceCacheFixture : CoreTest<StudioService>
    {
        private Mock<ICached> _v3Cache;
        private Mock<ICached> _v5Cache;

        [SetUp]
        public void Setup()
        {
            _v3Cache = new Mock<ICached>();
            _v5Cache = new Mock<ICached>();

            Mocker.GetMock<ICacheManager>()
                .Setup(c => c.FindCache(ApiResourceCaches.Studio[0]))
                .Returns(_v3Cache.Object);
            Mocker.GetMock<ICacheManager>()
                .Setup(c => c.FindCache(ApiResourceCaches.Studio[1]))
                .Returns(_v5Cache.Object);
        }

        private static Movie GivenScene(string studioForeignId)
        {
            return new Movie
            {
                MovieMetadata = new MovieMetadata { StudioForeignId = studioForeignId }
            };
        }

        [Test]
        public void should_drop_cached_studio_when_one_of_its_scenes_is_updated()
        {
            Subject.Handle(new MovieUpdatedEvent(GivenScene("studio-1")));

            _v3Cache.Verify(c => c.Remove("studio-1"), Times.Once());
            _v5Cache.Verify(c => c.Remove("studio-1"), Times.Once());
        }

        [Test]
        public void should_drop_each_cached_studio_once_when_scenes_are_added()
        {
            var scenes = new List<Movie> { GivenScene("studio-1"), GivenScene("studio-1"), GivenScene("studio-2") };

            Subject.Handle(new MoviesImportedEvent(scenes));

            _v5Cache.Verify(c => c.Remove("studio-1"), Times.Once());
            _v5Cache.Verify(c => c.Remove("studio-2"), Times.Once());
        }

        [TestCase(null)]
        [TestCase("")]
        public void should_ignore_scene_without_studio(string studioForeignId)
        {
            Subject.Handle(new MovieUpdatedEvent(GivenScene(studioForeignId)));

            _v5Cache.Verify(c => c.Remove(It.IsAny<string>()), Times.Never());
        }
    }
}
