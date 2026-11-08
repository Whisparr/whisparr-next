using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SkyHookProxyJavFixture : CoreTest<SkyHookProxy>
    {
        private const string JavId = "8e91a563-8a9a-40db-a737-36a663b3ae1f";
        private const string StudioId = "39cee498-a9ac-4403-910a-1a0157ad22d8";

        private readonly List<HttpRequest> _requests = new();

        [SetUp]
        public void Setup()
        {
            // CoreTest would otherwise hand back a mocked cache that stores nothing.
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            _requests.Clear();
        }

        [Test]
        public void get_jav_info_maps_type_source_code_and_strips_code_from_title()
        {
            Given<MovieResource>(HttpStatusCode.OK, Jav().ToJson());

            var movie = Subject.GetJavInfo(JavId).Item1;

            movie.ItemType.Should().Be(ItemType.Jav);
            movie.MetadataSource.Should().Be(Core.Movies.MetadataSource.TpdbJav);
            movie.ForeignId.Should().Be(JavId);
            movie.Code.Should().Be("ABP-123");
            movie.Title.Should().Be("Momo Sakai Perfect Satisfaction Fresh Face Soapland Deluxe");
            movie.StudioForeignId.Should().Be(StudioId);
            _requests.Should().ContainSingle().Which.Url.Path.Should().EndWith($"tpdb/jav/{JavId}");
        }

        [Test]
        public void get_jav_info_404_throws_movie_not_found()
        {
            Given<MovieResource>(HttpStatusCode.NotFound, "");

            Assert.Throws<MovieNotFoundException>(() => Subject.GetJavInfo(JavId));
        }

        [Test]
        public void studio_jav_page_requests_tpdb_site_jav_full_with_cursor()
        {
            Given<ScenePageResource>(HttpStatusCode.OK, new ScenePageResource { Items = new List<MovieResource>() }.ToJson());

            Subject.GetStudioJavPage(StudioId, "abc");

            var url = _requests.Should().ContainSingle().Subject.Url;
            url.Path.Should().EndWith($"tpdb/site/{StudioId}/jav/full");
            url.Query.Should().Contain("cursor=abc");
        }

        [Test]
        public void studio_jav_page_503_is_throttled()
        {
            Given<ScenePageResource>(HttpStatusCode.ServiceUnavailable, "");

            Subject.GetStudioJavPage(StudioId, null).Status.Should().Be(ScenePageStatus.Throttled);
        }

        [Test]
        public void performer_jav_page_requests_tpdb_performer_jav_full()
        {
            Given<ScenePageResource>(HttpStatusCode.OK, new ScenePageResource { Items = new List<MovieResource>() }.ToJson());

            Subject.GetPerformerJavPage("perf", null);

            _requests.Should().ContainSingle().Which.Url.Path.Should().EndWith("tpdb/performer/perf/jav/full");
        }

        [Test]
        public void search_for_new_entity_jav_calls_jav_search_only()
        {
            Given<List<MovieResource>>(HttpStatusCode.OK, "[]");

            Subject.SearchForNewEntity("ABP-123", ItemType.Jav).Should().BeEmpty();

            var url = _requests.Should().ContainSingle().Subject.Url;
            url.Path.Should().EndWith("tpdb/jav/search");
            url.Query.Should().Contain("q=ABP123");
        }

        [Test]
        public void search_for_new_entity_jav_looks_up_a_bare_uuid()
        {
            Given<MovieResource>(HttpStatusCode.OK, Jav().ToJson());

            var result = Subject.SearchForNewEntity(JavId, ItemType.Jav);

            result.Should().ContainSingle().Which.Should().BeOfType<Movie>().Which.MovieMetadata.Value.ForeignId.Should().Be(JavId);
            _requests.Should().ContainSingle().Which.Url.Path.Should().EndWith($"tpdb/jav/{JavId}");
        }

        [TestCase("true", true)]
        [TestCase("false", false)]
        [TestCase(null, null)]
        public void studio_info_maps_has_jav(string hasJav, bool? expected)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrSceneMetadataSource).Returns(SceneMetadataType.Tpdb);

            var field = hasJav == null ? "" : $",\"hasJav\":{hasJav}";
            Given<StudioResource>(HttpStatusCode.OK, $"{{\"title\":\"Studio\",\"foreignIds\":{{\"tpdbId\":\"{StudioId}\"}}{field}}}");

            Subject.GetStudioInfo(StudioId).HasJavReported.Should().Be(expected);
        }

        [Test]
        public void studio_has_jav_reads_jav_count()
        {
            Given<JavCountResource>(HttpStatusCode.OK, "{\"HasJav\":true,\"Total\":688}");

            Subject.GetStudioHasJav(StudioId).Should().BeTrue();
            _requests.Should().ContainSingle().Which.Url.Path.Should().EndWith($"tpdb/site/{StudioId}/jav/count");
        }

        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.NotFound)]
        public void studio_has_jav_is_null_on_error_status(HttpStatusCode status)
        {
            Given<JavCountResource>(status, "");

            Subject.GetStudioHasJav(StudioId).Should().BeNull();
        }

        [Test]
        public void performer_has_jav_is_null_when_request_throws()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<JavCountResource>(It.IsAny<HttpRequest>()))
                .Throws(new WebException("boom"));

            Subject.GetPerformerHasJav("perf").Should().BeNull();
        }

        [Test]
        public void changed_jav_requests_tpdb_jav_changed()
        {
            Given<List<string>>(HttpStatusCode.OK, "[\"a\",\"b\"]");

            Subject.GetChangedJav(new DateTime(2026, 1, 1, 10, 30, 0, DateTimeKind.Utc)).Should().BeEquivalentTo("a", "b");
            _requests.Should().ContainSingle().Which.Url.Path.Should().EndWith("tpdb/jav/changed");
        }

        [Test]
        public void deserializes_unknown_jav_item_type()
        {
            Json.Deserialize<MovieResource>("{\"itemType\":\"Jav\"}").ItemType.Should().Be(ItemType.Jav);
        }

        private void Given<T>(HttpStatusCode status, string body)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<T>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), body, status));
                });
        }

        private static MovieResource Jav()
        {
            return new MovieResource
            {
                ItemType = ItemType.Jav,
                ForeignIds = new ExternalIdResource { TpdbId = JavId },
                Code = "ABP-123",
                Title = "ABP-123 - Momo Sakai Perfect Satisfaction Fresh Face Soapland Deluxe",
                Images = new List<ImageResource>(),
                Genres = new List<string>(),
                Credits = new List<CastResource>(),
                Studio = new StudioResource
                {
                    Title = "Prestige",
                    ForeignIds = new ExternalIdResource { TpdbId = StudioId }
                }
            };
        }
    }
}
