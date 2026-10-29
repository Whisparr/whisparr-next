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

            var body = Json.Deserialize<Dictionary<string, object>>(System.Text.Encoding.UTF8.GetString(_requests[0].ContentData));
            body["kind"].Should().Be("site");
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

        [TestCase(HttpStatusCode.Accepted)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public void accepted_sets_pending_and_keeps_partial(HttpStatusCode status)
        {
            GivenResolve(ids => (status, new Dictionary<int, string> { { ids[0], "partial" } }));

            var result = Subject.Resolve(TpdbIdKind.Scene, new[] { 1, 2 });

            result.Pending.Should().BeTrue();
            result.Ids.Should().ContainKey(1).WhoseValue.Should().Be("partial");
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
