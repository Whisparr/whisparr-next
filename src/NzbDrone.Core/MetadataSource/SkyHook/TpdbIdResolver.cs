using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    public enum TpdbIdKind
    {
        Scene,
        Site,
        Performer
    }

    public class TpdbResolveResult
    {
        public Dictionary<int, string> Ids { get; set; } = new();

        // The server is still resolving, or throttling us; ask again later.
        public bool Pending { get; set; }
    }

    // Skyhook binds this body case-sensitively. The app's camel-case resolver overrides explicit
    // [JsonProperty] names, so opt the type out of it to keep PascalCase on the wire.
    [JsonObject(NamingStrategyType = typeof(DefaultNamingStrategy))]
    public class TpdbResolveRequest
    {
        public string Kind { get; set; }
        public List<int> Ids { get; set; }
    }

    public class TpdbResolveResponse
    {
        public Dictionary<int, string> Ids { get; set; } = new();
    }

    public interface ITpdbIdResolver
    {
        TpdbResolveResult Resolve(TpdbIdKind kind, IReadOnlyCollection<int> ids);
    }

    // Turns v2's numeric TPDB ids into TPDB UUIDs through the Skyhook metadata server.
    // Callers treat Pending and exceptions as transient.
    public class TpdbIdResolver : ITpdbIdResolver
    {
        private const string RouteConst = "route";
        private const int BatchSize = 1000;

        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _whisparrMetadata;

        public TpdbIdResolver(IHttpClient httpClient, IConfigFileProvider configFileProvider)
        {
            _httpClient = httpClient;

            var whisparrMetadata = configFileProvider.WhisparrMetadata;
            if (whisparrMetadata.IsNullOrWhiteSpace())
            {
                whisparrMetadata = "https://api.whisparr.com/v5/{route}";
            }

            _whisparrMetadata = new HttpRequestBuilder(whisparrMetadata).CreateFactory();
        }

        public TpdbResolveResult Resolve(TpdbIdKind kind, IReadOnlyCollection<int> ids)
        {
            var result = new TpdbResolveResult();
            var valid = ids.Where(i => i > 0).Distinct().ToList();

            foreach (var batch in valid.Chunk(BatchSize))
            {
                var body = new TpdbResolveRequest
                {
                    Kind = kind.ToString().ToLowerInvariant(),
                    Ids = batch.ToList()
                };

                var httpRequest = _whisparrMetadata.Create()
                                                   .SetSegment(RouteConst, "tpdb/resolve")
                                                   .Build();

                httpRequest.Headers.ContentType = "application/json";
                var json = body.ToJson(Formatting.None);

                httpRequest.SetContent(json);
                httpRequest.ContentSummary = json;
                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpError = true;

                var httpResponse = _httpClient.Post<TpdbResolveResponse>(httpRequest);

                switch (httpResponse.StatusCode)
                {
                    case HttpStatusCode.OK:
                        break;
                    case HttpStatusCode.Accepted:
                    case HttpStatusCode.TooManyRequests:
                    case HttpStatusCode.ServiceUnavailable:
                        result.Pending = true;
                        break;
                    default:
                        throw new HttpException(httpRequest, httpResponse);
                }

                if (httpResponse.Resource?.Ids != null)
                {
                    foreach (var pair in httpResponse.Resource.Ids)
                    {
                        result.Ids[pair.Key] = pair.Value;
                    }
                }
            }

            return result;
        }
    }
}
