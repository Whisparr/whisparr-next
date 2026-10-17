using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Cache;

namespace NzbDrone.Api.Test.v5
{
    // Guards against a resource rename silently orphaning a cache Core invalidates by name
    [TestFixture]
    public class ApiResourceCacheNamesFixture
    {
        [Test]
        public void should_name_real_movie_resource_caches()
        {
            ApiResourceCaches.Movie.Should()
                .BeEquivalentTo(
                    typeof(Whisparr.Api.V3.Movies.MovieResource).FullName + "_movieResources",
                    typeof(Whisparr.Api.V5.Movies.MovieResource).FullName + "_movieResources");
        }

        [Test]
        public void should_name_real_performer_resource_caches()
        {
            ApiResourceCaches.Performer.Should()
                .BeEquivalentTo(
                    typeof(Whisparr.Api.V3.Performers.PerformerResource).FullName + "_performerResources",
                    typeof(Whisparr.Api.V5.Performers.PerformerResource).FullName + "_performerResources");
        }

        [Test]
        public void should_name_real_studio_resource_caches()
        {
            ApiResourceCaches.Studio.Should()
                .BeEquivalentTo(
                    typeof(Whisparr.Api.V3.Studios.StudioResource).FullName + "_studioResources",
                    typeof(Whisparr.Api.V5.Studios.StudioResource).FullName + "_studioResources");
        }
    }
}
