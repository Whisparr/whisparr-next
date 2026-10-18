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
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SkyHookProxyScenePageFixture : CoreTest<SkyHookProxy>
    {
        private const string StudioId = "39cee498-a9ac-4403-910a-1a0157ad22d8";

        private HttpRequest _pageRequest;
        private List<string> _bulkBodies;

        [SetUp]
        public void Setup()
        {
            // CoreTest would otherwise hand back a mocked cache that stores nothing.
            Mocker.SetConstant<ICacheManager>(new CacheManager());

            _pageRequest = null;
            _bulkBodies = new List<string>();
        }

        [Test]
        public void should_request_site_scenes_full_with_cursor()
        {
            GivenPage(HttpStatusCode.OK, new ScenePageResource { Items = new List<MovieResource>() });

            Subject.GetStudioScenePage(StudioId, null);

            _pageRequest.Url.Path.Should().EndWith($"site/{StudioId}/scenes/full");
            _pageRequest.Url.Query.Should().NotContain("cursor");
            _pageRequest.Url.Query.Should().NotContain("limit");

            Subject.GetStudioScenePage(StudioId, "abc");

            _pageRequest.Url.Query.Should().Contain("cursor=abc");
            _pageRequest.Url.Query.Should().NotContain("limit");
        }

        [Test]
        public void should_map_complete_page()
        {
            GivenPage(
                HttpStatusCode.OK,
                new ScenePageResource
                {
                    Total = 12,
                    NextCursor = "next",
                    Items = new List<MovieResource> { Scene("a", "p1", "p2"), Scene("b", "p3") }
                });

            var page = Subject.GetStudioScenePage(StudioId, null);

            page.Status.Should().Be(ScenePageStatus.Complete);
            page.Total.Should().Be(12);
            page.NextCursor.Should().Be("next");
            page.Items.Select(i => i.StashId).Should().Equal("a", "b");
            page.Items[0].PerformerStashIds.Should().Equal("p1", "p2");
            page.Items[1].PerformerStashIds.Should().Equal("p3");
        }

        [Test]
        public void should_map_pending_page_with_retry_after()
        {
            GivenPage(
                HttpStatusCode.Accepted,
                new ScenePageResource
                {
                    Total = 5,
                    NextCursor = "next",
                    Items = new List<MovieResource> { Scene("a", "p1") },
                    Pending = new List<string> { "x" }
                },
                new HttpHeader { { "Retry-After", "7" } });

            var page = Subject.GetStudioScenePage(StudioId, null);

            page.Status.Should().Be(ScenePageStatus.Pending);
            page.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
            page.Pending.Should().Equal("x");
            page.Items.Select(i => i.StashId).Should().Equal("a");
            page.NextCursor.Should().Be("next");
        }

        [Test]
        public void should_default_pending_retry_after_to_five_seconds()
        {
            GivenPage(HttpStatusCode.Accepted, new ScenePageResource { Pending = new List<string> { "x" } });

            Subject.GetPerformerScenePage("perf", null).RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
            _pageRequest.Url.Path.Should().EndWith("performer/perf/scenes/full");
        }

        [Test]
        public void should_map_throttled_page()
        {
            GivenPageBody(HttpStatusCode.ServiceUnavailable, "", new HttpHeader());

            var page = Subject.GetStudioScenePage(StudioId, null);

            page.Status.Should().Be(ScenePageStatus.Throttled);
            page.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        }

        [Test]
        public void should_throw_not_found_for_unknown_studio()
        {
            GivenPageBody(HttpStatusCode.NotFound, "", new HttpHeader());

            Assert.Throws<MovieNotFoundException>(() => Subject.GetStudioScenePage(StudioId, null));
        }

        [Test]
        public void should_throw_http_exception_for_bad_cursor()
        {
            GivenPageBody(HttpStatusCode.BadRequest, "", new HttpHeader());

            Assert.Throws<HttpException>(() => Subject.GetStudioScenePage(StudioId, "bad"));
        }

        [Test]
        public void should_serve_scene_info_from_a_fetched_page_without_http()
        {
            var scene = Scene("a", "p1");
            GivenPage(HttpStatusCode.OK, new ScenePageResource { Items = new List<MovieResource> { scene } });
            GivenSceneGet(scene);

            Subject.GetStudioScenePage(StudioId, null);

            var fromCache = Subject.GetSceneInfo("a");

            Mocker.GetMock<IHttpClient>().Verify(v => v.Get<MovieResource>(It.IsAny<HttpRequest>()), Times.Never());

            Mocker.GetMock<IHttpClient>().Invocations.Clear();
            var fromHttp = Subject.GetSceneInfo("b");

            fromCache.Item1.StashId.Should().Be("a");
            fromCache.Item1.Title.Should().Be(fromHttp.Item1.Title);
            fromCache.Item3.Select(p => p.ForeignId).Should().Equal("p1");
            fromCache.Item4.Should().HaveCount(1);
        }

        [Test]
        public void should_map_a_fresh_instance_on_every_cached_read()
        {
            GivenPage(HttpStatusCode.OK, new ScenePageResource { Items = new List<MovieResource> { Scene("a", "p1") } });

            Subject.GetStudioScenePage(StudioId, null);

            Subject.GetSceneInfo("a").Item1.Should().NotBeSameAs(Subject.GetSceneInfo("a").Item1);
        }

        [Test]
        public void should_fetch_scene_over_http_when_not_prefetched()
        {
            GivenSceneGet(Scene("b", "p1"));

            Subject.GetSceneInfo("b");

            Mocker.GetMock<IHttpClient>().Verify(
                v => v.Get<MovieResource>(It.Is<HttpRequest>(r => r.Url.Path.EndsWith("scene/b"))),
                Times.Once());
        }

        [Test]
        public void should_prefetch_performers_in_batches_of_100()
        {
            GivenBulk(_ => new PerformerBulkResource { Performers = new List<PerformerResource>() });

            Subject.PrefetchPerformers(Enumerable.Range(0, 250).Select(i => $"p{i}").Concat(new[] { "p0" }));

            _bulkBodies.Select(b => Json.Deserialize<List<string>>(b).Count).Should().Equal(100, 100, 50);
        }

        [Test]
        public void should_return_pending_and_failed_performer_ids()
        {
            var ids = Enumerable.Range(0, 150).Select(i => $"p{i}").ToList();
            var call = 0;

            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Post<PerformerBulkResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    call++;

                    if (call == 1)
                    {
                        var body = new PerformerBulkResource
                        {
                            Performers = ids.Take(100).Where(i => i != "p2").Select(Performer).ToList(),
                            Pending = new List<string> { "p2" }
                        };

                        return Response<PerformerBulkResource>(request, HttpStatusCode.Accepted, body.ToJson(), new HttpHeader());
                    }

                    return Response<PerformerBulkResource>(request, HttpStatusCode.InternalServerError, "", new HttpHeader());
                });

            var result = Subject.PrefetchPerformers(ids);

            result.Should().BeEquivalentTo(new[] { "p2" }.Concat(ids.Skip(100)));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_serve_performer_info_from_prefetch_without_http()
        {
            GivenBulk(_ => new PerformerBulkResource { Performers = new List<PerformerResource> { Performer("p1") } });

            Subject.PrefetchPerformers(new[] { "p1" }).Should().BeEmpty();

            Subject.GetPerformerInfo("p1").ForeignId.Should().Be("p1");

            Mocker.GetMock<IHttpClient>().Verify(v => v.Get<PerformerResource>(It.IsAny<HttpRequest>()), Times.Never());
        }

        private static HttpResponse<T> Response<T>(HttpRequest request, HttpStatusCode status, string body, HttpHeader headers)
            where T : new()
        {
            return new HttpResponse<T>(new HttpResponse(request, headers, body, status));
        }

        private static PerformerResource Performer(string stashId)
        {
            return new PerformerResource
            {
                Name = $"Name {stashId}",
                ForeignIds = new ExternalIdResource { StashId = stashId },
                Images = new List<ImageResource>(),
                Aliases = new List<string>(),
                Tattoos = new List<string>(),
                Piercings = new List<string>()
            };
        }

        private static MovieResource Scene(string stashId, params string[] performerIds)
        {
            return new MovieResource
            {
                ItemType = ItemType.Scene,
                ForeignIds = new ExternalIdResource { StashId = stashId },
                Title = $"Scene {stashId}",
                ReleaseDate = "2024-05-17",
                Images = new List<ImageResource>(),
                Genres = new List<string>(),
                Studio = new StudioResource
                {
                    Title = "Studio",
                    ForeignIds = new ExternalIdResource { StashId = StudioId }
                },
                Credits = performerIds.Select((p, i) => new CastResource
                {
                    Order = i,
                    PersonName = $"Name {p}",
                    Performer = Performer(p)
                }).ToList()
            };
        }

        private void GivenPage(HttpStatusCode status, ScenePageResource body, HttpHeader headers = null)
        {
            GivenPageBody(status, body.ToJson(), headers ?? new HttpHeader());
        }

        private void GivenPageBody(HttpStatusCode status, string body, HttpHeader headers)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<ScenePageResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _pageRequest = request;
                    return Response<ScenePageResource>(request, status, body, headers);
                });
        }

        private void GivenSceneGet(MovieResource scene)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<MovieResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => Response<MovieResource>(request, HttpStatusCode.OK, scene.ToJson(), new HttpHeader()));
        }

        private void GivenBulk(Func<List<string>, PerformerBulkResource> respond)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Post<PerformerBulkResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    var body = System.Text.Encoding.UTF8.GetString(request.ContentData);
                    _bulkBodies.Add(body);

                    return Response<PerformerBulkResource>(request, HttpStatusCode.OK, respond(Json.Deserialize<List<string>>(body)).ToJson(), new HttpHeader());
                });
        }
    }
}
