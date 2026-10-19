using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using MovieMetadataSource = NzbDrone.Core.Movies.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SkyHookProxyMapForeignIdFixture
    {
        private static readonly ExternalIdResource ExternalIds = new()
        {
            StashId = "3f8a2c1e-5b7d-4e9a-8c6f-1d2e3f4a5b6c",
            TmdbId = 42019,
            TpdbId = "d534a2e3-b77a-4aac-95f6-e5671faf7165"
        };

        [Test]
        public void should_prefix_tpdb_movies_with_lowercase_tpdbid()
        {
            SkyHookProxy.MapForeignId(MovieMetadataSource.TpdbMovie, ExternalIds)
                .Should().Be("tpdbid:d534a2e3-b77a-4aac-95f6-e5671faf7165");
        }

        [Test]
        public void should_use_tmdb_id_for_tmdb_movies()
        {
            SkyHookProxy.MapForeignId(MovieMetadataSource.Tmdb, ExternalIds).Should().Be("42019");
        }

        [Test]
        public void should_use_stash_id_for_scenes()
        {
            SkyHookProxy.MapForeignId(MovieMetadataSource.Stash, ExternalIds).Should().Be("3f8a2c1e-5b7d-4e9a-8c6f-1d2e3f4a5b6c");
        }

        [TestCase(MovieMetadataSource.Stash, null)]
        [TestCase(MovieMetadataSource.Stash, " ")]
        [TestCase(MovieMetadataSource.TpdbScene, null)]
        [TestCase(MovieMetadataSource.TpdbScene, "")]
        public void should_not_key_a_scene_without_an_id_in_its_source(MovieMetadataSource source, string id)
        {
            var ids = new ExternalIdResource { StashId = id, TpdbId = id, TmdbId = 42019 };

            SkyHookProxy.MapForeignId(source, ids).Should().BeNull();
        }

        [Test]
        public void should_not_map_a_scene_without_an_id_in_the_locked_source()
        {
            var resource = new MovieResource
            {
                ItemType = ItemType.Scene,
                ForeignIds = new ExternalIdResource { StashId = "3f8a2c1e-5b7d-4e9a-8c6f-1d2e3f4a5b6c" },
                Title = "Scene",
                Images = new List<ImageResource>(),
                Genres = new List<string>()
            };

            SkyHookProxy.MapMovie(resource, SceneMetadataType.Tpdb).Should().BeNull();
            SkyHookProxy.MapMovie(resource, SceneMetadataType.StashDb).Should().NotBeNull();
        }
    }
}
