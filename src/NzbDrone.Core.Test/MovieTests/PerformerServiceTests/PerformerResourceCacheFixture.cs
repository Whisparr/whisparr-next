using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.PerformerServiceTests
{
    [TestFixture]
    public class PerformerResourceCacheFixture : CoreTest<PerformerService>
    {
        private Mock<ICached> _v3Cache;
        private Mock<ICached> _v5Cache;

        [SetUp]
        public void Setup()
        {
            _v3Cache = new Mock<ICached>();
            _v5Cache = new Mock<ICached>();

            Mocker.GetMock<ICacheManager>()
                .Setup(c => c.FindCache(ApiResourceCaches.Performer[0]))
                .Returns(_v3Cache.Object);
            Mocker.GetMock<ICacheManager>()
                .Setup(c => c.FindCache(ApiResourceCaches.Performer[1]))
                .Returns(_v5Cache.Object);
        }

        private static Movie GivenScene(params string[] performerForeignIds)
        {
            return new Movie
            {
                MovieMetadata = new MovieMetadata { PerformerForeignIds = new List<string>(performerForeignIds) }
            };
        }

        [Test]
        public void should_drop_every_credited_performer_when_a_scene_is_updated()
        {
            Subject.Handle(new MovieUpdatedEvent(GivenScene("performer-1", "performer-2")));

            _v3Cache.Verify(c => c.Remove("performer-1"), Times.Once());
            _v5Cache.Verify(c => c.Remove("performer-1"), Times.Once());
            _v5Cache.Verify(c => c.Remove("performer-2"), Times.Once());
        }

        [Test]
        public void should_drop_each_cached_performer_once_when_scenes_are_added()
        {
            var scenes = new List<Movie> { GivenScene("performer-1"), GivenScene("performer-1", "performer-2") };

            Subject.Handle(new MoviesImportedEvent(scenes));

            _v5Cache.Verify(c => c.Remove("performer-1"), Times.Once());
            _v5Cache.Verify(c => c.Remove("performer-2"), Times.Once());
        }

        [Test]
        public void should_ignore_scene_without_performers()
        {
            var scene = new Movie { MovieMetadata = new MovieMetadata { PerformerForeignIds = null } };

            Subject.Handle(new MovieUpdatedEvent(scene));

            _v5Cache.Verify(c => c.Remove(It.IsAny<string>()), Times.Never());
        }
    }
}
