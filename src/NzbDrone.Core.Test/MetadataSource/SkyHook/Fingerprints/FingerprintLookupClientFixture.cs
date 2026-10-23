using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook.Fingerprints
{
    [TestFixture]
    public class FingerprintLookupClientFixture : CoreTest<FingerprintLookupClient>
    {
        private List<HttpRequest> _requests;
        private Func<HttpRequest, HttpResponse> _respond;

        [SetUp]
        public void Setup()
        {
            _requests = new List<HttpRequest>();
            _respond = EchoMatched;

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrSceneMetadataSource)
                  .Returns(SceneMetadataType.StashDb);

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r =>
                  {
                      _requests.Add(r);
                      return _respond(r);
                  });
        }

        [Test]
        public void should_post_documented_json_and_map_response()
        {
            _respond = r => Json(r, @"{ ""files"": [
                { ""key"": ""a"", ""status"": ""matched"", ""candidates"": [
                    { ""sceneId"": ""s1"", ""algorithm"": ""OSHASH"", ""distance"": 0, ""submissions"": 3, ""duration"": 1800 } ] },
                { ""key"": ""b"", ""status"": ""NotFound"", ""candidates"": [] } ] }");

            var result = Subject.Lookup(
                new[]
                {
                    new FileFingerprint { Key = "a", OsHash = "abc", PHash = "def", Duration = 1800.5 },
                    new FileFingerprint { Key = "b", OsHash = "xyz" }
                },
                true);

            _requests.Should().HaveCount(1);
            _requests[0].Url.Path.Should().EndWith("fingerprint/lookup");

            var body = JObject.Parse(Body(_requests[0]));
            body["source"].Value<string>().Should().Be("stashDb");
            body["localOnly"].Value<bool>().Should().BeTrue();
            var sent = (JArray)body["files"];
            sent.Should().HaveCount(2);
            sent[0]["key"].Value<string>().Should().Be("a");
            sent[0]["oshash"].Value<string>().Should().Be("abc");
            sent[0]["phash"].Value<string>().Should().Be("def");
            sent[0]["duration"].Value<double>().Should().Be(1800.5);
            sent[1]["phash"].Should().BeNull();

            result["a"].Status.Should().Be(FingerprintLookupStatus.Matched);
            result["a"].Candidates.Should().ContainSingle();
            var c = result["a"].Candidates[0];
            c.SceneId.Should().Be("s1");
            c.Algorithm.Should().Be("OSHASH");
            c.Submissions.Should().Be(3);
            c.Duration.Should().Be(1800);
            result["b"].Status.Should().Be(FingerprintLookupStatus.NotFound);
        }

        [Test]
        public void should_send_tpdb_source_from_config()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrSceneMetadataSource)
                  .Returns(SceneMetadataType.Tpdb);

            Subject.Lookup(Files(1), false);

            JObject.Parse(Body(_requests[0]))["source"].Value<string>().Should().Be("tpdb");
            JObject.Parse(Body(_requests[0]))["localOnly"].Value<bool>().Should().BeFalse();
        }

        [Test]
        public void should_not_send_files_without_hashes()
        {
            var result = Subject.Lookup(
                new[]
                {
                    new FileFingerprint { Key = "none" },
                    new FileFingerprint { Key = "blank", OsHash = " ", PHash = "" }
                },
                true);

            _requests.Should().BeEmpty();
            result.Should().HaveCount(2);
            result.Values.Should().OnlyContain(r => r.Status == FingerprintLookupStatus.NotFound);
        }

        [Test]
        public void should_batch_local_only_by_100()
        {
            var result = Subject.Lookup(Files(250), true);

            Sizes().Should().Equal(100, 100, 50);
            result.Should().HaveCount(250);
        }

        [Test]
        public void should_batch_tpdb_upstream_by_20()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrSceneMetadataSource)
                  .Returns(SceneMetadataType.Tpdb);

            Subject.Lookup(Files(45), false);

            Sizes().Should().Equal(20, 20, 5);
        }

        [Test]
        public void should_send_45_stashdb_upstream_in_one_request()
        {
            Subject.Lookup(Files(45), false);

            Sizes().Should().Equal(45);
        }

        [Test]
        public void should_batch_tpdb_local_only_by_100()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrSceneMetadataSource)
                  .Returns(SceneMetadataType.Tpdb);

            Subject.Lookup(Files(45), true);

            Sizes().Should().Equal(45);
        }

        [Test]
        public void should_return_try_later_for_failed_batch_and_map_others()
        {
            var call = 0;
            _respond = r => ++call == 2 ? throw new HttpException(r, new HttpResponse(r, new HttpHeader(), "", HttpStatusCode.InternalServerError)) : EchoMatched(r);

            var result = Subject.Lookup(Files(250), true);

            result.Should().HaveCount(250);
            result.Values.Count(v => v.Status == FingerprintLookupStatus.TryLater).Should().Be(100);
            result["f0"].Status.Should().Be(FingerprintLookupStatus.Matched);
            result["f150"].Status.Should().Be(FingerprintLookupStatus.TryLater);
            result["f249"].Status.Should().Be(FingerprintLookupStatus.Matched);
        }

        [Test]
        public void should_return_try_later_on_timeout()
        {
            _respond = r => throw new WebException("timed out", WebExceptionStatus.Timeout);

            var result = Subject.Lookup(Files(3), true);

            result.Values.Should().OnlyContain(v => v.Status == FingerprintLookupStatus.TryLater);
        }

        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.BadGateway)]
        [TestCase(HttpStatusCode.NotFound)]
        public void should_return_try_later_on_non_success_status(HttpStatusCode status)
        {
            _respond = r => new HttpResponse(r, new HttpHeader(), "{}", status);

            var result = Subject.Lookup(Files(2), true);

            result.Values.Should().OnlyContain(v => v.Status == FingerprintLookupStatus.TryLater);
        }

        [Test]
        public void should_return_try_later_on_malformed_json()
        {
            _respond = r => Json(r, "{ not json");

            var result = Subject.Lookup(Files(2), true);

            result.Values.Should().OnlyContain(v => v.Status == FingerprintLookupStatus.TryLater);
        }

        [Test]
        public void should_return_try_later_for_key_missing_from_response()
        {
            _respond = r => Json(r, @"{ ""files"": [ { ""key"": ""f0"", ""status"": ""matched"", ""candidates"": [] } ] }");

            var result = Subject.Lookup(Files(2), true);

            result["f0"].Status.Should().Be(FingerprintLookupStatus.Matched);
            result["f1"].Status.Should().Be(FingerprintLookupStatus.TryLater);
        }

        [TestCase("matched", FingerprintLookupStatus.Matched)]
        [TestCase("MATCHED", FingerprintLookupStatus.Matched)]
        [TestCase("notfound", FingerprintLookupStatus.NotFound)]
        [TestCase("NotFound", FingerprintLookupStatus.NotFound)]
        [TestCase("tryLater", FingerprintLookupStatus.TryLater)]
        [TestCase("TRYLATER", FingerprintLookupStatus.TryLater)]
        [TestCase("somethingNew", FingerprintLookupStatus.TryLater)]
        public void should_map_status_case_insensitively(string wire, FingerprintLookupStatus expected)
        {
            _respond = r => Json(r, $@"{{ ""files"": [ {{ ""key"": ""f0"", ""status"": ""{wire}"" }} ] }}");

            var result = Subject.Lookup(Files(1), true);

            result["f0"].Status.Should().Be(expected);
            result["f0"].Candidates.Should().BeEmpty();
        }

        [Test]
        public void should_set_upstream_request_timeout_to_180_seconds()
        {
            Subject.Lookup(Files(1), false);

            _requests[0].RequestTimeout.Should().Be(TimeSpan.FromSeconds(180));
        }

        [Test]
        public void should_set_local_only_request_timeout_to_15_seconds()
        {
            Subject.Lookup(Files(1), true);

            _requests[0].RequestTimeout.Should().Be(TimeSpan.FromSeconds(15));
        }

        [Test]
        public void should_not_send_remaining_batches_after_a_timeout()
        {
            _respond = r => throw new WebException("timed out", WebExceptionStatus.Timeout);

            var result = Subject.Lookup(Files(250), true);

            _requests.Should().HaveCount(1);
            result.Should().HaveCount(250);
            result.Values.Should().OnlyContain(v => v.Status == FingerprintLookupStatus.TryLater);
        }

        [Test]
        public void should_keep_sending_batches_after_a_non_success_response()
        {
            _respond = r => new HttpResponse(r, new HttpHeader(), "{}", HttpStatusCode.InternalServerError);

            var result = Subject.Lookup(Files(250), true);

            _requests.Should().HaveCount(3);
            result.Values.Should().OnlyContain(v => v.Status == FingerprintLookupStatus.TryLater);
        }

        [Test]
        public void should_return_empty_for_no_files()
        {
            Subject.Lookup(new List<FileFingerprint>(), true).Should().BeEmpty();
            _requests.Should().BeEmpty();
        }

        private static List<FileFingerprint> Files(int count)
        {
            return Enumerable.Range(0, count)
                             .Select(i => new FileFingerprint { Key = $"f{i}", OsHash = $"{i:x16}" })
                             .ToList();
        }

        private List<int> Sizes()
        {
            return _requests.Select(r => ((JArray)JObject.Parse(Body(r))["files"]).Count).ToList();
        }

        private static string Body(HttpRequest request)
        {
            return Encoding.UTF8.GetString(request.ContentData);
        }

        private static HttpResponse Json(HttpRequest request, string json)
        {
            var headers = new HttpHeader { ContentType = "application/json" };
            return new HttpResponse(request, headers, json);
        }

        private static HttpResponse EchoMatched(HttpRequest request)
        {
            var files = (JArray)JObject.Parse(Body(request))["files"];
            var response = new JObject
            {
                ["files"] = new JArray(files.Select(f => new JObject
                {
                    ["key"] = f["key"],
                    ["status"] = "matched",
                    ["candidates"] = new JArray()
                }))
            };

            return Json(request, response.ToString());
        }
    }
}
