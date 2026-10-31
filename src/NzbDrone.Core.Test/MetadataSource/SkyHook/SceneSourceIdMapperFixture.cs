using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.SkyHook
{
    [TestFixture]
    public class SceneSourceIdMapperFixture : CoreTest<SceneSourceIdMapper>
    {
        private const string StashId = "11111111-1111-1111-1111-111111111111";
        private const string TpdbId = "22222222-2222-2222-2222-222222222222";

        private List<HttpRequest> _requests;
        private Func<HttpRequest, (HttpStatusCode Status, object Body)> _respond;

        [SetUp]
        public void Setup()
        {
            _requests = new List<HttpRequest>();
            _respond = _ => (HttpStatusCode.NotFound, null);

            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<StudioResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) => Respond<StudioResource>(r));
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<PerformerResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) => Respond<PerformerResource>(r));
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<List<StudioResource>>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) => Respond<List<StudioResource>>(r));
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Get<List<PerformerResource>>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest r) => Respond<List<PerformerResource>>(r));
        }

        [Test]
        public void studio_mapped_by_foreign_ids()
        {
            _respond = _ => (HttpStatusCode.OK, new StudioResource
            {
                Title = "Brazzers",
                ForeignIds = new ExternalIdResource { StashId = StashId, TpdbId = TpdbId }
            });

            var result = Subject.MapStudio(SceneMetadataType.StashDb, StashId, "Brazzers");

            result.Should().Be(new IdMapResult(TpdbId, IdMapMethod.Mapping));
            _requests.Should().ContainSingle();
        }

        [Test]
        public void performer_mapped_by_foreign_ids()
        {
            _respond = _ => (HttpStatusCode.OK, new PerformerResource
            {
                Name = "Jane Doe",
                ForeignIds = new ExternalIdResource { StashId = StashId, TpdbId = TpdbId }
            });

            var result = Subject.MapPerformer(SceneMetadataType.Tpdb, TpdbId, "Jane Doe");

            result.Should().Be(new IdMapResult(StashId, IdMapMethod.Mapping));
        }

        [Test]
        public void falls_back_to_single_exact_title()
        {
            GivenSearchResults(Studio("Brazzers", TpdbId), Studio("Brazzers Exxtra", "other"));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, StashId, "Brazzers");

            result.Should().Be(new IdMapResult(TpdbId, IdMapMethod.Title));
        }

        [Test]
        public void studio_title_is_retried_without_spaces_when_the_first_search_misses()
        {
            GivenCompactSearchResults(Studio("NubilesET", TpdbId));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "Nubiles ET");

            result.Should().Be(new IdMapResult(TpdbId, IdMapMethod.Title));
            _requests.Should().HaveCount(2);
            _requests[1].Url.FullUri.Should().Contain("q=NubilesET");
        }

        [Test]
        public void studio_compact_search_with_two_exact_titles_is_ambiguous()
        {
            GivenCompactSearchResults(Studio("NubilesET", "a"), Studio("NUBILESET", "b"));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "Nubiles ET");

            result.Should().Be(new IdMapResult(null, IdMapMethod.Ambiguous));
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void studio_ambiguous_first_search_is_not_retried()
        {
            GivenSearchResults(Studio("Nubiles ET", "a"), Studio("NUBILES ET", "b"));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "Nubiles ET");

            result.Should().Be(new IdMapResult(null, IdMapMethod.Ambiguous));
            _requests.Should().ContainSingle();
        }

        [Test]
        public void studio_title_without_spaces_is_searched_once()
        {
            GivenSearchResults();

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "NubilesET");

            result.Method.Should().Be(IdMapMethod.None);
            _requests.Should().ContainSingle();
        }

        [Test]
        public void studio_compact_search_is_skipped_when_the_first_search_matches()
        {
            GivenSearchResults(Studio("Nubiles ET", TpdbId));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "Nubiles ET");

            result.Should().Be(new IdMapResult(TpdbId, IdMapMethod.Title));
            _requests.Should().ContainSingle();
        }

        [Test]
        public void performer_name_is_retried_without_spaces_when_the_first_search_misses()
        {
            _respond = r => (HttpStatusCode.OK, r.Url.FullUri.Contains("q=JaneDoe")
                ? new List<PerformerResource> { new PerformerResource { Name = "JaneDoe", ForeignIds = new ExternalIdResource { StashId = StashId } } }
                : new List<PerformerResource>());

            var result = Subject.MapPerformer(SceneMetadataType.Tpdb, null, "Jane Doe");

            result.Should().Be(new IdMapResult(StashId, IdMapMethod.Title));
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void mapping_without_target_id_falls_back_to_title()
        {
            _respond = r => r.Url.FullUri.Contains("/search")
                ? (HttpStatusCode.OK, new List<StudioResource> { Studio("Brazzers", TpdbId) })
                : (HttpStatusCode.OK, new StudioResource { ForeignIds = new ExternalIdResource { StashId = StashId } });

            var result = Subject.MapStudio(SceneMetadataType.StashDb, StashId, "Brazzers");

            result.Should().Be(new IdMapResult(TpdbId, IdMapMethod.Title));
        }

        [Test]
        public void two_exact_titles_is_ambiguous()
        {
            GivenSearchResults(Studio("Brazzers", "a"), Studio("BRAZZERS", "b"));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "Brazzers");

            result.Should().Be(new IdMapResult(null, IdMapMethod.Ambiguous));
        }

        [Test]
        public void same_id_repeated_counts_once()
        {
            GivenSearchResults(Studio("Brazzers", "a"), Studio("Brazzers", "a"));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, "", "Brazzers");

            result.Should().Be(new IdMapResult("a", IdMapMethod.Title));
        }

        [Test]
        public void near_title_is_none()
        {
            GivenSearchResults(Studio("Brazzers Exxtra", "a"), Studio("Brazer", "b"));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "Brazzers");

            result.Should().Be(new IdMapResult(null, IdMapMethod.None));
        }

        [Test]
        public void results_without_a_target_id_are_ignored()
        {
            GivenSearchResults(Studio("Brazzers", null));

            var result = Subject.MapStudio(SceneMetadataType.StashDb, null, "Brazzers");

            result.Method.Should().Be(IdMapMethod.None);
        }

        [Test]
        public void performer_title_fallback_uses_target_source_id()
        {
            _respond = _ => (HttpStatusCode.OK, new List<PerformerResource>
            {
                new PerformerResource { Name = "Jane Doe", ForeignIds = new ExternalIdResource { StashId = StashId } }
            });

            var result = Subject.MapPerformer(SceneMetadataType.Tpdb, null, "jane doe");

            result.Should().Be(new IdMapResult(StashId, IdMapMethod.Title));
        }

        [Test]
        public void not_found_id_falls_back_to_title()
        {
            _respond = r => r.Url.FullUri.Contains("/search")
                ? (HttpStatusCode.OK, new List<StudioResource> { Studio("Brazzers", TpdbId) })
                : (HttpStatusCode.NotFound, null);

            var result = Subject.MapStudio(SceneMetadataType.StashDb, StashId, "Brazzers");

            result.Should().Be(new IdMapResult(TpdbId, IdMapMethod.Title));
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void empty_from_id_skips_the_fetch()
        {
            GivenSearchResults(Studio("Brazzers", TpdbId));

            Subject.MapStudio(SceneMetadataType.StashDb, " ", "Brazzers");

            _requests.Should().ContainSingle();
            _requests[0].Url.FullUri.Should().Contain("/search");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("!!!")]
        public void blank_title_without_mapping_is_none_and_not_searched(string title)
        {
            GivenSearchResults();

            Subject.MapStudio(SceneMetadataType.StashDb, null, title).Should().Be(new IdMapResult(null, IdMapMethod.None));
            Subject.MapPerformer(SceneMetadataType.Tpdb, null, title).Should().Be(new IdMapResult(null, IdMapMethod.None));

            _requests.Should().BeEmpty();
        }

        [Test]
        public void blank_title_after_unmapped_fetch_is_none_and_not_searched()
        {
            _respond = _ => (HttpStatusCode.NotFound, null);

            Subject.MapStudio(SceneMetadataType.StashDb, StashId, " ").Method.Should().Be(IdMapMethod.None);
            Subject.MapPerformer(SceneMetadataType.StashDb, StashId, null).Method.Should().Be(IdMapMethod.None);

            _requests.Should().HaveCount(2);
            _requests.Should().OnlyContain(r => !r.Url.FullUri.Contains("/search"));
        }

        [Test]
        public void server_error_throws_http_exception()
        {
            _respond = _ => (HttpStatusCode.InternalServerError, null);

            Action action = () => Subject.MapStudio(SceneMetadataType.StashDb, StashId, "Brazzers");

            action.Should().Throw<HttpException>();
        }

        [Test]
        public void search_server_error_throws_http_exception()
        {
            _respond = _ => (HttpStatusCode.BadGateway, null);

            Action action = () => Subject.MapPerformer(SceneMetadataType.StashDb, null, "Jane Doe");

            action.Should().Throw<HttpException>();
        }

        [TestCase(SceneMetadataType.StashDb, "site/" + StashId)]
        [TestCase(SceneMetadataType.Tpdb, "tpdb/site/" + TpdbId)]
        public void routes_follow_from_source(SceneMetadataType from, string expectedPath)
        {
            var fromId = from == SceneMetadataType.StashDb ? StashId : TpdbId;
            GivenSearchResults();

            Subject.MapStudio(from, fromId, "Brazzers");

            _requests.Should().HaveCount(2);
            _requests[0].Url.Path.Should().EndWith(expectedPath);

            // The title search runs against the target source
            var searchPath = from == SceneMetadataType.StashDb ? "tpdb/site/search" : "site/search";
            _requests[1].Url.Path.Should().EndWith(searchPath);
            _requests[1].Url.FullUri.Should().Contain("q=Brazzers");
        }

        [TestCase(SceneMetadataType.StashDb, "performer/", "tpdb/performer/search")]
        [TestCase(SceneMetadataType.Tpdb, "tpdb/performer/", "performer/search")]
        public void performer_routes_follow_from_source(SceneMetadataType from, string fetchPrefix, string searchPath)
        {
            GivenSearchResults();

            Subject.MapPerformer(from, "abc", "Jane Doe");

            _requests[0].Url.Path.Should().EndWith(fetchPrefix + "abc");
            _requests[1].Url.Path.Should().EndWith(searchPath);
        }

        private static StudioResource Studio(string title, string tpdbId)
        {
            return new StudioResource { Title = title, ForeignIds = new ExternalIdResource { TpdbId = tpdbId } };
        }

        private void GivenSearchResults(params StudioResource[] results)
        {
            _respond = r => r.Url.FullUri.Contains("/search")
                ? (HttpStatusCode.OK, results.ToList())
                : (HttpStatusCode.NotFound, null);
        }

        // Only the search with the spaces taken out finds anything, like the server's "NubilesET"
        private void GivenCompactSearchResults(params StudioResource[] results)
        {
            _respond = r => (HttpStatusCode.OK, r.Url.FullUri.Contains("q=NubilesET") ? results.ToList() : new List<StudioResource>());
        }

        private HttpResponse<T> Respond<T>(HttpRequest request)
            where T : new()
        {
            _requests.Add(request);

            var (status, body) = _respond(request);
            var json = body == null ? "{}" : body.ToJson();

            return new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), json, status));
        }
    }
}
