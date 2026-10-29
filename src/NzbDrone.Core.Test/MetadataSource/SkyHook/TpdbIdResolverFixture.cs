using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class TpdbIdResolverFixture : CoreTest<TpdbIdResolver>
    {
        private List<HttpRequest> _requests;

        [SetUp]
        public void Setup()
        {
            _requests = new List<HttpRequest>();
        }

        [Test]
        public void should_post_kind_lowercased_to_resolve_route()
        {
            GivenResolve(ids => (HttpStatusCode.OK, ids.ToDictionary(i => i, i => $"uuid-{i}")));

            Subject.Resolve(TpdbIdKind.Site, new[] { 7 });

            _requests.Should().ContainSingle();
            _requests[0].Url.Path.Should().EndWith("tpdb/resolve");

            // Skyhook binds case-sensitively, so assert the wire text rather than round-tripping through app settings.
            var raw = System.Text.Encoding.UTF8.GetString(_requests[0].ContentData);
            raw.Should().Contain("\"Kind\":\"site\"");
        }

        [Test]
        public void should_send_request_body_in_pascal_case()
        {
            GivenResolve(ids => (HttpStatusCode.OK, new Dictionary<int, string>()));

            Subject.Resolve(TpdbIdKind.Site, new[] { 1 });

            var raw = System.Text.Encoding.UTF8.GetString(_requests[0].ContentData);
            raw.Should().Contain("\"Kind\":\"site\"");
            raw.Should().Contain("\"Ids\":[");
            raw.Should().NotContain("\"kind\"");
            raw.Should().NotContain("\"ids\"");
        }

        [Test]
        public void should_read_pascal_case_response_body()
        {
            var request = new HttpRequest("http://localhost/tpdb/resolve");
            var response = new HttpResponse(request, new HttpHeader(), "{\"Ids\":{\"123\":\"abc-uuid\"}}", HttpStatusCode.OK);

            // HttpResponse<T> deserializes with the same Json.Deserialize the real client uses.
            var typed = new HttpResponse<TpdbResolveResponse>(response);

            typed.Resource.Ids.Should().BeEquivalentTo(new Dictionary<int, string> { { 123, "abc-uuid" } });
        }

        [Test]
        public void should_return_map_on_ok()
        {
            GivenResolve(ids => (HttpStatusCode.OK, new Dictionary<int, string> { { 1, "a" } }));

            var result = Subject.Resolve(TpdbIdKind.Scene, new[] { 1, 2 });

            result.Pending.Should().BeFalse();
            result.Ids.Should().BeEquivalentTo(new Dictionary<int, string> { { 1, "a" } });
        }

        [Test]
        public void batches_by_1000()
        {
            var sizes = new List<int>();

            GivenResolve(ids =>
            {
                sizes.Add(ids.Count);
                return (HttpStatusCode.OK, ids.ToDictionary(i => i, i => $"uuid-{i}"));
            });

            var result = Subject.Resolve(TpdbIdKind.Scene, Enumerable.Range(1, 2500).ToList());

            sizes.Should().Equal(1000, 1000, 500);
            result.Ids.Should().HaveCount(2500);
        }

        [Test]
        public void drops_non_positive_ids()
        {
            var sent = new List<int>();

            GivenResolve(ids =>
            {
                sent.AddRange(ids);
                return (HttpStatusCode.OK, new Dictionary<int, string>());
            });

            Subject.Resolve(TpdbIdKind.Performer, new[] { -5, 0, 3, 4 });

            sent.Should().Equal(3, 4);
        }

        [Test]
        public void should_not_call_when_no_valid_ids()
        {
            GivenResolve(ids => (HttpStatusCode.OK, new Dictionary<int, string>()));

            var result = Subject.Resolve(TpdbIdKind.Scene, new[] { 0, -1 });

            result.Ids.Should().BeEmpty();
            result.Pending.Should().BeFalse();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void merges_partial_maps()
        {
            GivenResolve(ids => (HttpStatusCode.OK, ids.Where(i => i % 2 == 0).ToDictionary(i => i, i => $"uuid-{i}")));

            var result = Subject.Resolve(TpdbIdKind.Scene, Enumerable.Range(1, 1500).ToList());

            _requests.Should().HaveCount(2);
            result.Ids.Should().HaveCount(750);
            result.Ids[1500].Should().Be("uuid-1500");
            result.Ids.Should().NotContainKey(1);
        }

        [Test]
        public void accepted_sets_pending_and_keeps_partial()
        {
            GivenResolve(ids => (HttpStatusCode.Accepted, new Dictionary<int, string> { { ids[0], "partial" } }));

            var result = Subject.Resolve(TpdbIdKind.Scene, new[] { 1, 2 });

            result.Pending.Should().BeTrue();
            result.Throttled.Should().BeFalse();
            result.Ids.Should().ContainKey(1).WhoseValue.Should().Be("partial");
        }

        [Test]
        public void accepted_keeps_sending_the_remaining_batches()
        {
            GivenResolve(ids => (HttpStatusCode.Accepted, new Dictionary<int, string>()));

            Subject.Resolve(TpdbIdKind.Scene, Enumerable.Range(1, 2500).ToList());

            _requests.Should().HaveCount(3);
        }

        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public void throttled_is_not_pending_and_stops_further_batches(HttpStatusCode status)
        {
            GivenResolve(ids => ids.Contains(1)
                ? (HttpStatusCode.OK, new Dictionary<int, string> { { 1, "first" } })
                : (status, new Dictionary<int, string>()));

            var result = Subject.Resolve(TpdbIdKind.Scene, Enumerable.Range(1, 3500).ToList());

            result.Throttled.Should().BeTrue();
            result.Pending.Should().BeFalse();
            result.Ids.Should().ContainKey(1);
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void server_error_throws()
        {
            GivenResolve(ids => (HttpStatusCode.InternalServerError, new Dictionary<int, string>()));

            Action action = () => Subject.Resolve(TpdbIdKind.Scene, new[] { 1 });

            action.Should().Throw<HttpException>();
        }

        private void GivenResolve(Func<List<int>, (HttpStatusCode Status, Dictionary<int, string> Map)> respond)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Post<TpdbResolveResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);

                    var body = Json.Deserialize<TpdbResolveRequest>(System.Text.Encoding.UTF8.GetString(request.ContentData));
                    var (status, map) = respond(body.Ids);
                    var json = new TpdbResolveResponse { Ids = map }.ToJson();

                    return new HttpResponse<TpdbResolveResponse>(new HttpResponse(request, new HttpHeader(), json, status));
                });
        }
    }
}
