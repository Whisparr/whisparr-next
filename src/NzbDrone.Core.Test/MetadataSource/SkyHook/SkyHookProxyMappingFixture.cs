using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using MovieMetadataSource = NzbDrone.Core.Movies.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SkyHookProxyMappingFixture : CoreTest<SkyHookProxy>
    {
        private const string SceneStashId = "3f8a2c1e-5b7d-4e9a-8c6f-1d2e3f4a5b6c";
        private const string SceneTpdbId = "d534a2e3-b77a-4aac-95f6-e5671faf7165";
        private const string StudioStashId = "39cee498-a9ac-4403-910a-1a0157ad22d8";
        private const string StudioTpdbId = "7a1e0c55-2f3b-4d8e-9b6a-0c4d5e6f7a8b";
        private const string TagStashId = "a0c4b2d6-1e3f-4a5b-8c7d-9e0f1a2b3c4d";

        [SetUp]
        public void Setup()
        {
            // CoreTest would otherwise hand back a mocked cache that stores nothing.
            Mocker.SetConstant<ICacheManager>(new CacheManager());
        }

        [Test]
        public void should_key_tpdb_scene_by_tpdb_uuid()
        {
            var resource = Scene(stashId: null, tpdbId: SceneTpdbId);

            var movie = SkyHookProxy.MapMovie(resource, SceneMetadataType.Tpdb);

            movie.ForeignId.Should().Be(resource.ForeignIds.TpdbId);
            movie.MetadataSource.Should().Be(MovieMetadataSource.TpdbScene);
            movie.TpdbId.Should().Be(SceneTpdbId);
            movie.StashId.Should().BeNull();
            movie.StudioForeignId.Should().Be(StudioTpdbId);
            movie.StudioTitle.Should().Be("Studio");
        }

        [Test]
        public void should_keep_stash_id_from_skyhook_on_a_tpdb_scene()
        {
            var movie = SkyHookProxy.MapMovie(Scene(SceneStashId, SceneTpdbId), SceneMetadataType.Tpdb);

            movie.ForeignId.Should().Be(SceneTpdbId);
            movie.StashId.Should().Be(SceneStashId);
        }

        [Test]
        public void should_key_stash_scene_by_stash_id()
        {
            var movie = SkyHookProxy.MapMovie(Scene(SceneStashId, SceneTpdbId));

            movie.ForeignId.Should().Be(SceneStashId);
            movie.MetadataSource.Should().Be(MovieMetadataSource.Stash);
            movie.StashId.Should().Be(SceneStashId);
            movie.TpdbId.Should().Be(SceneTpdbId);
            movie.StudioForeignId.Should().Be(StudioStashId);
            movie.TagIds.Should().Equal(TagStashId);
        }

        [Test]
        public void should_leave_tag_ids_empty_under_tpdb()
        {
            var movie = SkyHookProxy.MapMovie(Scene(SceneStashId, SceneTpdbId), SceneMetadataType.Tpdb);

            movie.TagIds.Should().BeEmpty();
        }

        [Test]
        public void should_keep_studio_title_but_not_key_when_studio_lacks_locked_source_id()
        {
            var resource = Scene(stashId: null, tpdbId: SceneTpdbId);
            resource.Studio.ForeignIds = new ExternalIdResource { StashId = StudioStashId };

            var movie = SkyHookProxy.MapMovie(resource, SceneMetadataType.Tpdb);

            movie.StudioForeignId.Should().BeNull();
            movie.StudioTitle.Should().Be("Studio");
        }

        [Test]
        public void should_key_tpdb_studio_and_credits_by_tpdb_uuid()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);
            GivenSceneGet(Scene(stashId: null, tpdbId: SceneTpdbId, Cast("p1", "tp1"), Cast("p2", "tp2")));

            var (movie, studio, performers, credits) = Subject.GetSceneInfo(SceneTpdbId);

            movie.ForeignId.Should().Be(SceneTpdbId);
            movie.MetadataSource.Should().Be(MovieMetadataSource.TpdbScene);
            movie.StudioForeignId.Should().Be(StudioTpdbId);
            movie.PerformerForeignIds.Should().Equal("tp1", "tp2");

            studio.ForeignId.Should().Be(StudioTpdbId);
            studio.TpdbId.Should().Be(StudioTpdbId);

            performers.Select(p => p.ForeignId).Should().Equal("tp1", "tp2");
            performers.Select(p => p.TpdbId).Should().Equal("tp1", "tp2");

            credits.Select(c => c.PerformerForeignId).Should().Equal("tp1", "tp2");
            credits.Select(c => c.Performer.ForeignId).Should().Equal("tp1", "tp2");
        }

        [Test]
        public void should_skip_credits_without_an_id_in_the_locked_source()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);

            var resource = Scene(stashId: null, tpdbId: SceneTpdbId, Cast("p1", "tp1"), Cast("p2", null));
            resource.Studio.ForeignIds = new ExternalIdResource { StashId = StudioStashId };
            GivenSceneGet(resource);

            var (movie, studio, performers, credits) = Subject.GetSceneInfo(SceneTpdbId);

            credits.Select(c => c.PerformerForeignId).Should().Equal("tp1");
            performers.Select(p => p.ForeignId).Should().Equal("tp1");
            movie.PerformerForeignIds.Should().Equal("tp1");
            movie.PerformerNames.Should().Equal("Name p1");
            studio.Should().BeNull();
            movie.StudioForeignId.Should().BeNull();
        }

        [Test]
        public void should_skip_stash_credits_without_a_stash_id()
        {
            GivenSceneGet(Scene(SceneStashId, null, Cast("p1", "tp1"), Cast(null, "tp2")));

            var (_, _, performers, credits) = Subject.GetSceneInfo(SceneStashId);

            credits.Select(c => c.PerformerForeignId).Should().Equal("p1");
            performers.Select(p => p.ForeignId).Should().Equal("p1");
        }

        [Test]
        public void should_key_movie_credits_and_studio_by_the_locked_scene_source()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);

            var resource = Scene(null, "movie-tpdb", Cast("p1", "tp1"), Cast("p2", null));
            resource.ItemType = ItemType.Movie;
            GivenSceneGet(resource);

            var (movie, studio, performers, credits) = Subject.GetTpdbMovieInfo("movie-tpdb");

            movie.MetadataSource.Should().Be(MovieMetadataSource.TpdbMovie);
            movie.ForeignId.Should().Be("tpdbid:movie-tpdb");
            movie.StudioForeignId.Should().Be(StudioTpdbId);
            studio.ForeignId.Should().Be(StudioTpdbId);
            credits.Select(c => c.PerformerForeignId).Should().Equal("tp1");
            performers.Select(p => p.ForeignId).Should().Equal("tp1");
        }

        [Test]
        public void should_key_tpdb_performer_by_tpdb_uuid()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);
            GivenPerformerGet(Performer("p1", "tp1"));

            Subject.GetPerformerInfo("tp1").ForeignId.Should().Be("tp1");
        }

        [Test]
        public void should_treat_performer_without_locked_source_id_as_not_found()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);
            GivenPerformerGet(Performer("p1", null));

            Assert.Throws<MovieNotFoundException>(() => Subject.GetPerformerInfo("tp1"));
        }

        [Test]
        public void should_key_tpdb_studio_by_tpdb_uuid()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);
            GivenStudioGet(new StudioResource
            {
                Title = "Studio",
                ForeignIds = new ExternalIdResource { StashId = StudioStashId, TpdbId = StudioTpdbId }
            });

            Subject.GetStudioInfo(StudioTpdbId).ForeignId.Should().Be(StudioTpdbId);
        }

        [Test]
        public void should_map_tpdb_scene_foreign_id_to_bare_uuid()
        {
            SkyHookProxy.MapForeignId(MovieMetadataSource.TpdbScene, new ExternalIdResource { StashId = SceneStashId, TpdbId = SceneTpdbId })
                .Should().Be(SceneTpdbId);
        }

        [TestCase(SceneMetadataType.StashDb, MovieMetadataSource.Stash)]
        [TestCase(SceneMetadataType.Tpdb, MovieMetadataSource.TpdbScene)]
        public void should_map_scene_metadata_source_by_locked_source(SceneMetadataType source, MovieMetadataSource expected)
        {
            SceneSourceIds.SceneMetadataSource(source).Should().Be(expected);
            SkyHookProxy.MapMetadataSource(ItemType.Scene, new ExternalIdResource(), source).Should().Be(expected);
            SkyHookProxy.MapMetadataSource(ItemType.Scene, null, source).Should().Be(expected);
        }

        [Test]
        public void should_not_change_movie_metadata_source_under_tpdb_scenes()
        {
            SkyHookProxy.MapMetadataSource(ItemType.Movie, new ExternalIdResource { TmdbId = 1 }, SceneMetadataType.Tpdb)
                .Should().Be(MovieMetadataSource.Tmdb);
        }

        [TestCase(SceneMetadataType.StashDb, SceneStashId)]
        [TestCase(SceneMetadataType.Tpdb, SceneTpdbId)]
        public void should_pick_key_from_locked_source(SceneMetadataType source, string expected)
        {
            SceneSourceIds.Key(source, new ExternalIdResource { StashId = SceneStashId, TpdbId = SceneTpdbId }).Should().Be(expected);
        }

        [TestCase(SceneMetadataType.StashDb)]
        [TestCase(SceneMetadataType.Tpdb)]
        public void should_return_null_key_when_absent(SceneMetadataType source)
        {
            SceneSourceIds.Key(source, null).Should().BeNull();
            SceneSourceIds.Key(source, new ExternalIdResource { StashId = " ", TpdbId = "" }).Should().BeNull();
        }

        [TestCase("scene", "tpdb/scene")]
        [TestCase("scene/search", "tpdb/scene/search")]
        [TestCase("scene/changed", "tpdb/scene/changed")]
        [TestCase("site", "tpdb/site")]
        [TestCase("site/search", "tpdb/site/search")]
        [TestCase("site/changed", "tpdb/site/changed")]
        [TestCase("performer", "tpdb/performer")]
        [TestCase("performer/bulk", "tpdb/performer/bulk")]
        [TestCase("performer/search", "tpdb/performer/search")]
        [TestCase("performer/changed", "tpdb/performer/changed")]
        public void should_route_scene_paths_to_tpdb(string stashRoute, string expected)
        {
            SceneSourceIds.Route(SceneMetadataType.Tpdb, stashRoute).Should().Be(expected);
        }

        [TestCase("movie")]
        [TestCase("movie/bulk")]
        [TestCase("movie/changed")]
        [TestCase("movie/search")]
        [TestCase("movie/collection")]
        [TestCase("movie/imdb")]
        [TestCase("tpdb/movie")]
        [TestCase("tpdb/scene")]
        [TestCase("scenes")]
        [TestCase("")]
        [TestCase(null)]
        public void should_not_route_non_scene_paths_to_tpdb(string route)
        {
            SceneSourceIds.Route(SceneMetadataType.Tpdb, route).Should().Be(route);
        }

        [TestCase("scene")]
        [TestCase("site/search")]
        [TestCase("performer/bulk")]
        [TestCase("movie")]
        public void should_leave_routes_unchanged_under_stashdb(string route)
        {
            SceneSourceIds.Route(SceneMetadataType.StashDb, route).Should().Be(route);
        }

        private void GivenSceneSource(SceneMetadataType source)
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(s => s.WhisparrSceneMetadataSource)
                .Returns(source);
        }

        private void GivenSceneGet(MovieResource scene)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<MovieResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<MovieResource>(request, scene.ToJson()));
        }

        private void GivenPerformerGet(PerformerResource performer)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<PerformerResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<PerformerResource>(request, performer.ToJson()));
        }

        private void GivenStudioGet(StudioResource studio)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<StudioResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<StudioResource>(request, studio.ToJson()));
        }

        private static HttpResponse<T> Response<T>(HttpRequest request, string body)
            where T : new()
        {
            return new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), body, HttpStatusCode.OK));
        }

        private static PerformerResource Performer(string stashId, string tpdbId)
        {
            return new PerformerResource
            {
                Name = $"Name {stashId ?? tpdbId}",
                ForeignIds = new ExternalIdResource { StashId = stashId, TpdbId = tpdbId },
                Images = new List<ImageResource>(),
                Aliases = new List<string>(),
                Tattoos = new List<string>(),
                Piercings = new List<string>()
            };
        }

        private static CastResource Cast(string stashId, string tpdbId)
        {
            return new CastResource
            {
                PersonName = $"Name {stashId ?? tpdbId}",
                Performer = Performer(stashId, tpdbId)
            };
        }

        private static MovieResource Scene(string stashId, string tpdbId, params CastResource[] credits)
        {
            return new MovieResource
            {
                ItemType = ItemType.Scene,
                ForeignIds = new ExternalIdResource { StashId = stashId, TpdbId = tpdbId },
                Title = "Scene",
                ReleaseDate = "2024-05-17",
                Images = new List<ImageResource>(),
                Genres = new List<string>(),
                Tags = new List<TagResource>
                {
                    new() { Name = "Tag", ForeignIds = new ExternalIdResource { StashId = TagStashId } }
                },
                Studio = new StudioResource
                {
                    Title = "Studio",
                    ForeignIds = new ExternalIdResource { StashId = StudioStashId, TpdbId = StudioTpdbId }
                },
                Credits = credits.Select((c, i) =>
                {
                    c.Order = i;
                    return c;
                }).ToList()
            };
        }
    }
}
