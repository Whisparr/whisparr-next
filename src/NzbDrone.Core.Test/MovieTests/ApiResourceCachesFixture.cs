using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class ApiResourceCachesFixture : CoreTest
    {
        [Test]
        public void should_remove_key_from_every_existing_cache()
        {
            var cache = new Mock<ICached>();
            var cacheManager = new Mock<ICacheManager>();
            cacheManager.Setup(c => c.FindCache(ApiResourceCaches.Movie[0])).Returns(cache.Object);
            cacheManager.Setup(c => c.FindCache(ApiResourceCaches.Movie[1])).Returns((ICached)null);

            cacheManager.Object.Remove(ApiResourceCaches.Movie, "42");

            cache.Verify(c => c.Remove("42"), Times.Once());
        }

        [Test]
        public void should_remove_key_from_both_versions()
        {
            var v3 = new Mock<ICached>();
            var v5 = new Mock<ICached>();
            var cacheManager = new Mock<ICacheManager>();
            cacheManager.Setup(c => c.FindCache(ApiResourceCaches.Studio[0])).Returns(v3.Object);
            cacheManager.Setup(c => c.FindCache(ApiResourceCaches.Studio[1])).Returns(v5.Object);

            cacheManager.Object.Remove(ApiResourceCaches.Studio, "abc");

            v3.Verify(c => c.Remove("abc"), Times.Once());
            v5.Verify(c => c.Remove("abc"), Times.Once());
        }
    }
}
