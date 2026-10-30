using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    public enum IdMapMethod
    {
        None,
        Mapping,
        Title
    }

    // Id is null when Method is None.
    public sealed record IdMapResult(string Id, IdMapMethod Method);

    public interface ISceneSourceIdMapper
    {
        IdMapResult MapStudio(SceneMetadataType from, string fromId, string title);
        IdMapResult MapPerformer(SceneMetadataType from, string fromId, string name);
    }

    // Maps a studio or performer id from one scene source to the other through the Skyhook metadata server.
    // The server's own StashDB/TPDB cross-references win; failing that, a single exact title match in the
    // target source does. A 404 on the lookup falls through to the title search; any other failure throws
    // so the caller can treat it as transient.
    public class SceneSourceIdMapper : ISceneSourceIdMapper
    {
        private const string RouteConst = "route";

        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _whisparrMetadata;

        public SceneSourceIdMapper(IHttpClient httpClient, IConfigFileProvider configFileProvider)
        {
            _httpClient = httpClient;

            var whisparrMetadata = configFileProvider.WhisparrMetadata;
            if (whisparrMetadata.IsNullOrWhiteSpace())
            {
                whisparrMetadata = "https://api.whisparr.com/v5/{route}";
            }

            _whisparrMetadata = new HttpRequestBuilder(whisparrMetadata).CreateFactory();
        }

        public IdMapResult MapStudio(SceneMetadataType from, string fromId, string title)
        {
            var to = Other(from);

            if (fromId.IsNotNullOrWhiteSpace())
            {
                var studio = Get<StudioResource>(from, "site", fromId);
                var mapped = ForeignId(studio?.ForeignIds, to);

                if (mapped.IsNotNullOrWhiteSpace())
                {
                    return new IdMapResult(mapped, IdMapMethod.Mapping);
                }
            }

            var results = Search<StudioResource>(to, "site/search", title) ?? new List<StudioResource>();

            return ByTitle(title, to, results.Select(s => (s.Title, s.ForeignIds)));
        }

        public IdMapResult MapPerformer(SceneMetadataType from, string fromId, string name)
        {
            var to = Other(from);

            if (fromId.IsNotNullOrWhiteSpace())
            {
                var performer = Get<PerformerResource>(from, "performer", fromId);
                var mapped = ForeignId(performer?.ForeignIds, to);

                if (mapped.IsNotNullOrWhiteSpace())
                {
                    return new IdMapResult(mapped, IdMapMethod.Mapping);
                }
            }

            var results = Search<PerformerResource>(to, "performer/search", name) ?? new List<PerformerResource>();

            return ByTitle(name, to, results.Select(p => (p.Name, p.ForeignIds)));
        }

        private static SceneMetadataType Other(SceneMetadataType source)
        {
            return source == SceneMetadataType.Tpdb ? SceneMetadataType.StashDb : SceneMetadataType.Tpdb;
        }

        private static string ForeignId(ExternalIdResource ids, SceneMetadataType source)
        {
            return source == SceneMetadataType.Tpdb ? ids?.TpdbId : ids?.StashId;
        }

        private static IdMapResult ByTitle(string title, SceneMetadataType target, IEnumerable<(string Title, ExternalIdResource Ids)> candidates)
        {
            var clean = title.CleanMovieTitle();
            var ids = new List<string>();

            if (clean.IsNotNullOrWhiteSpace())
            {
                ids = candidates.Where(c => c.Title.CleanMovieTitle() == clean)
                                .Select(c => ForeignId(c.Ids, target))
                                .Where(id => id.IsNotNullOrWhiteSpace())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }

            return ids.Count == 1
                ? new IdMapResult(ids[0], IdMapMethod.Title)
                : new IdMapResult(null, IdMapMethod.None);
        }

        // Null when the source has no such record.
        private T Get<T>(SceneMetadataType source, string route, string id)
            where T : class, new()
        {
            var request = _whisparrMetadata.Create()
                                           .SetSegment(RouteConst, SceneSourceIds.Route(source, route))
                                           .Resource(id)
                                           .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<T>(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.HasHttpError)
            {
                throw new HttpException(request, response);
            }

            return response.Resource;
        }

        private List<T> Search<T>(SceneMetadataType source, string route, string term)
        {
            var request = _whisparrMetadata.Create()
                                           .SetSegment(RouteConst, SceneSourceIds.Route(source, route))
                                           .AddQueryParam("q", term ?? string.Empty)
                                           .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<List<T>>(request);

            if (response.HasHttpError)
            {
                throw new HttpException(request, response);
            }

            return response.Resource;
        }
    }
}
