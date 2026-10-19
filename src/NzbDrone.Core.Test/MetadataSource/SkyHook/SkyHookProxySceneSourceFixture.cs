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
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SkyHookProxySceneSourceFixture : CoreTest<SkyHookProxy>
    {
        private const string SceneTpdbId = "d534a2e3-b77a-4aac-95f6-e5671faf7165";
        private const string SceneStashId = "3f8a2c1e-5b7d-4e9a-8c6f-1d2e3f4a5b6c";
        private const string StudioTpdbId = "7a1e0c55-2f3b-4d8e-9b6a-0c4d5e6f7a8b";
        private const string OtherStudioTpdbId = "5b1a7f0c-2a3e-4d7a-9e1b-6c0d8f4a2b3c";
        private const string PerformerTpdbId = "0c8f6d1e-3a2b-4c5d-8e9f-a0b1c2d3e4f5";
        private const string FileName = "BrazzersExxtra.2024-05-17.Jane.Doe.Threeway.Tug.Of.War.1080p.mp4";

        private HttpRequest _request;

        [SetUp]
        public void Setup()
        {
            // CoreTest would otherwise hand back a mocked cache that stores nothing.
            Mocker.SetConstant<ICacheManager>(new CacheManager());

            _request = null;

            GivenSceneSource(SceneMetadataType.Tpdb);
        }

        [Test]
        public void should_fetch_tpdb_scene_from_tpdb_route()
        {
            GivenSceneGet(Scene(SceneTpdbId));

            var scene = Subject.GetSceneInfo(SceneTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/scene/{SceneTpdbId}");
            scene.Item1.ForeignId.Should().Be(SceneTpdbId);
        }

        [Test]
        public void should_fetch_stash_scene_from_stash_route()
        {
            GivenSceneSource(SceneMetadataType.StashDb);
            GivenSceneGet(Scene(SceneTpdbId));

            Subject.GetSceneInfo(SceneStashId).Item1.ForeignId.Should().Be(SceneStashId);

            _request.Url.Path.Should().EndWith($"scene/{SceneStashId}");
            _request.Url.Path.Should().NotContain("tpdb");
        }

        [Test]
        public void should_fetch_tpdb_performer_from_tpdb_route()
        {
            GivenGet(Performer(PerformerTpdbId));

            Subject.GetPerformerInfo(PerformerTpdbId).ForeignId.Should().Be(PerformerTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/performer/{PerformerTpdbId}");
        }

        [Test]
        public void should_fetch_tpdb_studio_from_tpdb_route()
        {
            GivenGet(Studio("Brazzers Exxtra", StudioTpdbId));

            Subject.GetStudioInfo(StudioTpdbId).ForeignId.Should().Be(StudioTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/site/{StudioTpdbId}");
        }

        [TestCase("scene")]
        [TestCase("site")]
        [TestCase("performer")]
        public void should_fetch_tpdb_changed_scenes_from_tpdb_route(string kind)
        {
            GivenGet(new List<string> { SceneTpdbId });

            var changed = GetChanged(kind);

            _request.Url.Path.Should().EndWith($"tpdb/{kind}/changed");
            _request.Url.Query.Should().Contain("since=");
            changed.Should().BeEquivalentTo(new[] { SceneTpdbId });
        }

        [Test]
        public void should_return_no_changes_when_the_tpdb_changed_route_fails()
        {
            GivenGet(new List<string>(), HttpStatusCode.NotFound);

            Subject.GetChangedScenes(DateTime.UtcNow).Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_fetch_tpdb_studio_works_from_tpdb_route()
        {
            GivenGet(new StudioWorksResource { TpdbMovies = new List<string> { "m1", "m2" }, Scenes = new List<string>(), Movies = new List<string>() });

            var works = Subject.GetStudioWorks(StudioTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/site/{StudioTpdbId}/works");
            works.TpdbIds.Should().Equal("m1", "m2");
            works.StashdbIds.Should().BeEmpty();
            works.TmdbIds.Should().BeEmpty();
        }

        [Test]
        public void should_fetch_tpdb_performer_works_from_tpdb_route()
        {
            GivenGet(new PerformerWorksResource { TpdbMovies = new List<string> { "m1" } });

            var works = Subject.GetPerformerWorks(PerformerTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/performer/{PerformerTpdbId}/works");
            works.TpdbIds.Should().Equal("m1");
            works.StashdbIds.Should().BeEmpty();
            works.TmdbIds.Should().BeEmpty();
        }

        [Test]
        public void should_treat_an_unknown_tpdb_works_owner_as_not_found()
        {
            // Skyhook answers an unknown UUID with a 404 that still carries an empty body.
            GivenGet(new StudioWorksResource { TpdbMovies = new List<string>() }, HttpStatusCode.NotFound);

            Assert.Throws<MovieNotFoundException>(() => Subject.GetStudioWorks(StudioTpdbId));
        }

        [TestCase(SceneTpdbId)]
        [TestCase("stash:" + SceneTpdbId)]
        [TestCase("stashid:" + SceneTpdbId)]
        [TestCase("tpdb:" + SceneTpdbId)]
        [TestCase("tpdbid:" + SceneTpdbId)]
        public void should_lookup_tpdb_scene_directly_for_guid_term(string term)
        {
            GivenSceneGet(Scene(SceneTpdbId));

            var results = Subject.SearchForNewEntity(term, ItemType.Scene).Cast<Movie>().ToList();

            _request.Url.Path.Should().EndWith($"tpdb/scene/{SceneTpdbId}");
            results.Select(m => m.MovieMetadata.Value.ForeignId).Should().Equal(SceneTpdbId);
            Mocker.GetMock<IHttpClient>().Verify(v => v.Get<List<MovieResource>>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_return_nothing_for_a_prefixed_tpdb_term_that_is_not_a_guid()
        {
            Subject.SearchForNewEntity("tpdb:12345", ItemType.Scene).Should().BeEmpty();

            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_return_an_owned_tpdb_scene_as_the_existing_movie()
        {
            var existing = new Movie { Id = 7, MovieMetadata = new MovieMetadata { ForeignId = SceneTpdbId, Title = "Owned" } };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByForeignId(SceneTpdbId)).Returns(existing);
            GivenSceneGet(Scene(SceneTpdbId));

            var results = Subject.SearchForNewEntity($"tpdb:{SceneTpdbId}", ItemType.Scene).Cast<Movie>().ToList();

            results.Should().ContainSingle().Which.Should().BeSameAs(existing);
        }

        [Test]
        public void should_return_an_owned_tpdb_scene_from_a_title_search_as_the_existing_movie()
        {
            var existing = new Movie { Id = 7, MovieMetadata = new MovieMetadata { ForeignId = SceneTpdbId, Title = "Owned" } };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByForeignId(SceneTpdbId)).Returns(existing);
            GivenSearchReturns(Scene(SceneTpdbId));

            var results = Subject.SearchForNewEntity("threeway tug of war", ItemType.Scene).Cast<Movie>().ToList();

            results.Should().ContainSingle().Which.Should().BeSameAs(existing);
        }

        [Test]
        public void should_search_tpdb_scenes_by_title_on_the_tpdb_route()
        {
            GivenSearchReturns(Scene(SceneTpdbId));

            Subject.SearchForNewEntity("threeway tug of war", ItemType.Scene).Should().HaveCount(1);

            _request.Url.Path.Should().EndWith("tpdb/scene/search");
            QueryParams()["q"].Should().Be("threeway tug of war");
        }

        [Test]
        public void should_send_combined_query_for_tpdb_studio_date_search()
        {
            Mocker.GetMock<IStudioService>()
                .Setup(v => v.FindByTitle(It.IsAny<string>()))
                .Returns(new Studio { Title = "Brazzers Exxtra", CleanTitle = "brazzersexxtra", ForeignId = StudioTpdbId });
            GivenSearchReturns(
                Scene(SceneTpdbId, studioTpdbId: StudioTpdbId),
                Scene("e1f2a3b4-c5d6-4e7f-8a9b-0c1d2e3f4a5b", studioTpdbId: OtherStudioTpdbId));

            var results = Subject.SearchForNewEntity(FileName, ItemType.Scene).Cast<Movie>().ToList();

            _request.Url.Path.Should().EndWith("tpdb/scene/search");

            var query = QueryParams();
            query["q"].Should().Be("brazzers exxtra 2024-05-17 jane doe threeway tug of war");
            query.Should().NotContainKey("studio");
            query.Should().NotContainKey("date");

            results.Select(m => m.MovieMetadata.Value.ForeignId).Should().Equal(SceneTpdbId);
        }

        [Test]
        public void should_resolve_an_unknown_studio_on_the_tpdb_route_for_a_studio_date_search()
        {
            GivenGet(new List<StudioResource> { Studio("Brazzers Exxtra", StudioTpdbId) });
            GivenSearchReturns();

            Subject.SearchForNewEntity(FileName, ItemType.Scene);

            Mocker.GetMock<IHttpClient>().Verify(
                v => v.Get<List<StudioResource>>(It.Is<HttpRequest>(r => r.Url.Path.EndsWith("tpdb/site/search"))),
                Times.Once());
            QueryParams()["q"].Should().StartWith("brazzers exxtra 2024-05-17");
        }

        [TestCase(PerformerTpdbId)]
        [TestCase("tpdb:" + PerformerTpdbId)]
        [TestCase("stashid:" + PerformerTpdbId)]
        public void should_lookup_tpdb_performer_directly_for_guid_term(string term)
        {
            GivenGet(Performer(PerformerTpdbId));

            Subject.SearchForNewPerformer(term).Select(p => p.ForeignId).Should().Equal(PerformerTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/performer/{PerformerTpdbId}");
        }

        [Test]
        public void should_search_tpdb_performers_by_name_on_the_tpdb_route()
        {
            GivenGet(new List<PerformerResource> { Performer(PerformerTpdbId) });

            Subject.SearchForNewPerformer("jane doe").Select(p => p.ForeignId).Should().Equal(PerformerTpdbId);

            _request.Url.Path.Should().EndWith("tpdb/performer/search");
        }

        [TestCase(StudioTpdbId)]
        [TestCase("tpdbid:" + StudioTpdbId)]
        [TestCase("stash:" + StudioTpdbId)]
        public void should_lookup_tpdb_studio_directly_for_guid_term(string term)
        {
            GivenGet(Studio("Brazzers Exxtra", StudioTpdbId));

            Subject.SearchForNewStudio(term).Select(s => s.ForeignId).Should().Equal(StudioTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/site/{StudioTpdbId}");
        }

        [Test]
        public void should_search_tpdb_studios_by_name_on_the_tpdb_route()
        {
            GivenGet(new List<StudioResource> { Studio("Brazzers Exxtra", StudioTpdbId) });

            Subject.SearchForNewStudio("brazzers exxtra").Select(s => s.ForeignId).Should().Equal(StudioTpdbId);

            _request.Url.Path.Should().EndWith("tpdb/site/search");
        }

        [Test]
        public void should_still_reject_tpdb_scene_ids_under_stashdb()
        {
            GivenSceneSource(SceneMetadataType.StashDb);

            Assert.Throws<InvalidSearchTermException>(() => Subject.SearchForNewEntity($"tpdb:{SceneTpdbId}", ItemType.Scene));
        }

        [Test]
        public void should_reject_tmdb_ids_in_a_tpdb_scene_search()
        {
            Assert.Throws<InvalidSearchTermException>(() => Subject.SearchForNewEntity("tmdb:123", ItemType.Scene));
        }

        // Review items from Task 2: keyless scenes, owned-scene lookups and search-result credits.
        [Test]
        public void should_treat_a_scene_without_a_locked_source_id_as_not_found()
        {
            GivenSceneGet(Scene(null, stashId: SceneStashId));

            Assert.Throws<MovieNotFoundException>(() => Subject.GetSceneInfo(SceneTpdbId));
        }

        [Test]
        public void should_drop_search_results_without_a_locked_source_id()
        {
            GivenSearchReturns(Scene(null, stashId: SceneStashId), Scene(SceneTpdbId));

            var results = Subject.SearchForNewEntity("threeway tug of war", ItemType.Scene).Cast<Movie>().ToList();

            results.Select(m => m.MovieMetadata.Value.ForeignId).Should().Equal(SceneTpdbId);
            Mocker.GetMock<IMovieService>().Verify(s => s.FindByForeignId(null), Times.Never());
        }

        [Test]
        public void should_build_search_result_performer_names_from_keyed_credits()
        {
            var scene = Scene(SceneTpdbId);
            scene.Credits = new List<CastResource>
            {
                new() { PersonName = "Keyed", Performer = Performer(PerformerTpdbId) },
                new() { PersonName = "Stash Only", Performer = Performer(null, "stash-only") },
                new() { PersonName = "No Performer", Performer = null }
            };
            GivenSearchReturns(scene);

            var movie = Subject.SearchForNewEntity("threeway tug of war", ItemType.Scene).Cast<Movie>().Single().MovieMetadata.Value;

            movie.PerformerNames.Should().Equal("Keyed");
            movie.PerformerForeignIds.Should().Equal(PerformerTpdbId);
            movie.Credits.Select(c => c.PerformerForeignId).Should().Equal(PerformerTpdbId);
        }

        [Test]
        public void should_read_the_scene_source_once_per_search()
        {
            GivenSearchReturns(Scene(SceneTpdbId), Scene("e1f2a3b4-c5d6-4e7f-8a9b-0c1d2e3f4a5b"));

            Subject.SearchForNewEntity("threeway tug of war", ItemType.Scene);

            Mocker.GetMock<IConfigService>().VerifyGet(s => s.WhisparrSceneMetadataSource, Times.Once());
        }

        [Test]
        public void should_map_an_import_list_tpdb_scene_by_its_tpdb_id()
        {
            var existing = new MovieMetadata { ForeignId = SceneTpdbId, Title = "Owned" };
            Mocker.GetMock<IMovieMetadataService>().Setup(s => s.FindByForeignId(SceneTpdbId)).Returns(existing);

            Subject.MapMovieToTmdbMovie(new MovieMetadata { ItemType = ItemType.Scene, TpdbId = SceneTpdbId, Title = "Owned" })
                .Should().BeSameAs(existing);
        }

        [Test]
        public void should_fetch_an_import_list_tpdb_scene_from_the_tpdb_route()
        {
            GivenSceneGet(Scene(SceneTpdbId));

            Subject.MapMovieToTmdbMovie(new MovieMetadata { ItemType = ItemType.Scene, TpdbId = SceneTpdbId, Title = "Scene" })
                .ForeignId.Should().Be(SceneTpdbId);

            _request.Url.Path.Should().EndWith($"tpdb/scene/{SceneTpdbId}");
        }

        private HashSet<string> GetChanged(string kind)
        {
            return kind switch
            {
                "scene" => Subject.GetChangedScenes(DateTime.UtcNow),
                "site" => Subject.GetChangedStudios(DateTime.UtcNow),
                _ => Subject.GetChangedPerformers(DateTime.UtcNow)
            };
        }

        private void GivenSceneSource(SceneMetadataType source)
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(s => s.WhisparrSceneMetadataSource)
                .Returns(source);
        }

        private void GivenSceneGet(MovieResource scene)
        {
            GivenGet(scene);
        }

        private void GivenSearchReturns(params MovieResource[] scenes)
        {
            GivenGet(scenes.ToList());
        }

        private void GivenGet<T>(T body, HttpStatusCode status = HttpStatusCode.OK)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<T>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _request = request;

                    return new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), body.ToJson(), status));
                });
        }

        private Dictionary<string, string> QueryParams()
        {
            _request.Should().NotBeNull();

            return _request.Url.Query
                .Split('&')
                .Select(p => p.Split('='))
                .ToDictionary(p => p[0], p => WebUtility.UrlDecode(p[1]));
        }

        private static PerformerResource Performer(string tpdbId, string stashId = null)
        {
            return new PerformerResource
            {
                Name = $"Name {tpdbId ?? stashId}",
                ForeignIds = new ExternalIdResource { TpdbId = tpdbId, StashId = stashId ?? $"stash-{tpdbId}" },
                Images = new List<ImageResource>(),
                Aliases = new List<string>(),
                Tattoos = new List<string>(),
                Piercings = new List<string>()
            };
        }

        private static StudioResource Studio(string title, string tpdbId)
        {
            return new StudioResource
            {
                Title = title,
                ForeignIds = new ExternalIdResource { TpdbId = tpdbId, StashId = $"stash-{tpdbId}" },
                Images = new List<ImageResource>()
            };
        }

        private static MovieResource Scene(string tpdbId, string stashId = SceneStashId, string studioTpdbId = StudioTpdbId)
        {
            return new MovieResource
            {
                ItemType = ItemType.Scene,
                ForeignIds = new ExternalIdResource { TpdbId = tpdbId, StashId = stashId },
                Title = "Threeway Tug Of War",
                ReleaseDate = "2024-05-17",
                Images = new List<ImageResource>(),
                Genres = new List<string>(),
                Studio = new StudioResource
                {
                    Title = "Brazzers Exxtra",
                    ForeignIds = new ExternalIdResource { TpdbId = studioTpdbId, StashId = $"stash-{studioTpdbId}" }
                },
                Credits = new List<CastResource>()
            };
        }
    }
}
