using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class SceneSourceLinksFixture : CoreTest
    {
        private const string ForeignId = "6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41";

        [TestCase(Movies.MetadataSource.Stash, "https://stashdb.org/scenes/" + ForeignId, "StashDB")]
        [TestCase(Movies.MetadataSource.TpdbScene, "https://theporndb.net/scenes/" + ForeignId, "TPDB")]
        [TestCase(Movies.MetadataSource.Tmdb, null, null)]
        [TestCase(Movies.MetadataSource.TpdbMovie, null, null)]
        public void should_link_by_metadata_source(Movies.MetadataSource source, string expectedUrl, string expectedName)
        {
            var metadata = new MovieMetadata { MetadataSource = source, ForeignId = ForeignId };

            SceneSourceLinks.GetSceneUrl(metadata).Should().Be(expectedUrl);
            SceneSourceLinks.GetSceneSiteName(metadata).Should().Be(expectedName);
        }

        [Test]
        public void should_return_null_without_metadata()
        {
            SceneSourceLinks.GetSceneUrl(null).Should().BeNull();
            SceneSourceLinks.GetSceneSiteName(null).Should().BeNull();
        }
    }
}
