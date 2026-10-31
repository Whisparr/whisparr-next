using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Collections;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    public class SkyHookProxy : IProvideMovieInfo, ISearchForNewMovie
    {
        private const string RouteConst = "route";
        private const string TmdbPrefix = "tmdb:";
        private const string TmdbIdPrefix = "tmdbid:";
        private const string TpdbPrefix = "tpdb:";
        private const string TpdbIdPrefix = "tpdbid:";
        private const string SkyHookExceptionMessageConst = "Search for '{0}' failed. Unable to communicate with StashDb.";
        private const string SkyHookConnectionExceptionConst = "Search for '{0}' failed. Unable to communicate with WhisparrAPI. {1}";
        private const string SinceConst = "since";
        private const string StashPrefixConst = "stash:";
        private const string StashIdPrefixConst = "stashid:";
        private const int PerformerBulkBatchSize = 100;
        private static readonly string[] TpdbLookupPrefixes = { StashPrefixConst, StashIdPrefixConst, TpdbPrefix, TpdbIdPrefix };
        private static readonly TimeSpan PrefetchLifetime = TimeSpan.FromHours(2);
        private static readonly TimeSpan MinRetryAfter = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IHttpRequestBuilderFactory _whisparrMetadata;
        private readonly IConfigService _configService;
        private readonly IMovieService _movieService;
        private readonly IMovieMetadataService _movieMetadataService;
        private readonly IStudioService _studioService;
        private readonly ICached<MovieResource> _scenePrefetch;
        private readonly ICached<PerformerResource> _performerPrefetch;

        public SkyHookProxy(IHttpClient httpClient,
            IWhisparrCloudRequestBuilder requestBuilder,
            IConfigService configService,
            IConfigFileProvider configFileProvider,
            IMovieService movieService,
            IMovieMetadataService movieMetadataService,
            IStudioService studioService,
            ICacheManager cacheManager,
            Logger logger)
        {
            _httpClient = httpClient;

            var whisparrMetadata = configFileProvider.WhisparrMetadata;
            if (whisparrMetadata.IsNullOrWhiteSpace())
            {
                whisparrMetadata = "https://api.whisparr.com/v5/{route}";
            }

            logger.Info($"Using WhisparrMetadata {whisparrMetadata}");
            _whisparrMetadata = new HttpRequestBuilder(whisparrMetadata)
                .CreateFactory();
            _configService = configService;
            _movieService = movieService;
            _movieMetadataService = movieMetadataService;
            _studioService = studioService;
            _scenePrefetch = cacheManager.GetCache<MovieResource>(GetType(), "scenePrefetch");
            _performerPrefetch = cacheManager.GetCache<PerformerResource>(GetType(), "performerPrefetch");

            _logger = logger;
        }

        public static string MapForeignId(Movies.MetadataSource metadataSource, ExternalIdResource externalIdResource)
        {
            if (externalIdResource == null)
            {
                throw new ArgumentNullException(nameof(externalIdResource));
            }

            if (metadataSource == Movies.MetadataSource.Tmdb)
            {
                return externalIdResource.TmdbId.ToString();
            }
            else if (metadataSource == Movies.MetadataSource.TpdbMovie)
            {
                return $"{TpdbIdPrefix}{externalIdResource.TpdbId}";
            }
            else if (metadataSource == Movies.MetadataSource.TpdbScene)
            {
                return SceneSourceIds.Key(SceneMetadataType.Tpdb, externalIdResource);
            }
            else
            {
                return SceneSourceIds.Key(SceneMetadataType.StashDb, externalIdResource);
            }
        }

        // Returns null for a scene with no id in the locked scene source; it must not be stored keyless.
        public static MovieMetadata MapMovie(MovieResource resource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            var movie = new MovieMetadata();
            var metadataSource = MapMetadataSource(resource.ItemType, resource.ForeignIds, sceneSource);
            var foriegnId = MapForeignId(metadataSource, resource.ForeignIds);

            if (foriegnId == null && resource.ItemType != ItemType.Movie)
            {
                return null;
            }

            movie.ItemType = resource.ItemType;
            movie.MetadataSource = metadataSource;

            movie.ForeignId = foriegnId;
            movie.TmdbId = resource.ForeignIds.TmdbId;
            movie.TpdbId = resource.ForeignIds.TpdbId;
            movie.StashId = resource.ForeignIds.StashId;
            movie.Title = resource.Title;
            movie.CleanTitle = resource.Title.CleanMovieTitle();
            movie.SortTitle = Parser.Parser.NormalizeTitle(resource.Title);
            movie.Code = resource.Code;
            movie.Overview = resource.Overview;

            if (resource.AlternativeTitles != null)
            {
                movie.AlternativeTitles.AddRange(resource.AlternativeTitles.Select(MapAlternativeTitle));
            }

            movie.Website = resource.Homepage;
            movie.ReleaseDate = resource.ReleaseDate;
            movie.ReleaseDateUtc = resource.ReleaseDateUtc;

            movie.Year = resource.Year;

            if (resource.Duration != null)
            {
                movie.Runtime = resource.Duration.Value;
            }

            movie.Ratings = MapRatings(resource.Ratings) ?? new Ratings();

            movie.Genres = resource.Genres;

            // Check for tags with StashIDs as used for exclusion/tagging in Whisparr not stored locally.
            // Tag exclusion is StashDB-based, so a TPDB-locked library carries no tag ids.
            if (sceneSource == SceneMetadataType.StashDb && resource.Tags != null && resource.Tags.Any(t => !string.IsNullOrEmpty(t.ForeignIds?.StashId)))
            {
                movie.TagIds = resource.Tags.Select(t => t.ForeignIds.StashId).ToList();
            }
            else
            {
                movie.TagIds = new List<string>();
            }

            movie.Images = resource.Images.Select(MapImage).ToList();

            movie.ItemType = resource.ItemType;

            var now = DateTime.UtcNow;

            movie.Status = MovieStatusType.Announced;

            if (resource.ReleaseDateUtc.HasValue && now > resource.ReleaseDateUtc)
            {
                movie.Status = MovieStatusType.Released;
            }

            MapMovieStudio(movie, resource.Studio, sceneSource);

            // Collections only currently on TMDB, as other providers may implement.
            if (resource.Collection?.Name != null && resource.Collection?.ForeignIds?.TmdbId != null)
            {
                movie.CollectionTmdbId = resource.Collection.ForeignIds.TmdbId;
                movie.CollectionTitle = resource.Collection.Name;
            }

            return movie;
        }

        private static void MapMovieStudio(MovieMetadata movie, StudioResource studio, SceneMetadataType sceneSource)
        {
            var foreignIds = studio?.ForeignIds;

            if (foreignIds == null)
            {
                return;
            }

            var studioKey = SceneSourceIds.Key(sceneSource, foreignIds);

            if (studioKey != null)
            {
                movie.StudioForeignId = studioKey;
                movie.StudioTitle = studio.Title;
                movie.Studio = studio;
            }
            else if (foreignIds.TmdbId > 0 || !string.IsNullOrWhiteSpace(foreignIds.TpdbId) || !string.IsNullOrWhiteSpace(foreignIds.StashId))
            {
                movie.StudioTitle = studio.Title;
                movie.Studio = studio;
            }
        }

        public static Movies.MetadataSource MapMetadataSource(ItemType itemType, ExternalIdResource externalIdResource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            if (externalIdResource == null)
            {
                return itemType == ItemType.Movie ? Movies.MetadataSource.Tmdb : SceneSourceIds.SceneMetadataSource(sceneSource);
            }

            return itemType switch
            {
                ItemType.Movie => !string.IsNullOrWhiteSpace(externalIdResource.TpdbId)
                    ? Movies.MetadataSource.TpdbMovie
                    : Movies.MetadataSource.Tmdb,
                _ => SceneSourceIds.SceneMetadataSource(sceneSource)
            };
        }

        public HashSet<int> GetChangedMovies(DateTime startTime)
        {
            // Round down to the hour to ensure we cover gap and don't kill cache every call
            var cacheAdjustedStart = startTime.AddMinutes(-15);
            var startDate = cacheAdjustedStart.Date.AddHours(cacheAdjustedStart.Hour).ToString("s");

            var request = _whisparrMetadata.Create()
                .SetSegment(RouteConst, "movie/changed")
                .AddQueryParam(SinceConst, startDate)
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<List<int>>(request);

            if (response.HasHttpError)
            {
                _logger.Warn($"Error fetching changed movies from Skyhook: {request.Url} - {response.StatusCode}");
                return new HashSet<int>();
            }

            return new HashSet<int>(response.Resource);
        }

        public HashSet<string> GetChangedTpdbMovies(DateTime startTime)
        {
            // Round down to the hour to ensure we cover gap and don't kill cache every call
            var cacheAdjustedStart = startTime.AddMinutes(-15);
            var startDate = cacheAdjustedStart.Date.AddHours(cacheAdjustedStart.Hour).ToString("s");

            var request = _whisparrMetadata.Create()
                .SetSegment(RouteConst, "tpdb/movie/changed")
                .AddQueryParam(SinceConst, startDate)
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<List<string>>(request);

            if (response.HasHttpError)
            {
                _logger.Warn($"Error fetching changed TPDB movies from Skyhook: {request.Url} - {response.StatusCode}");
                return new HashSet<string>();
            }

            return new HashSet<string>(response.Resource);
        }

        public HashSet<string> GetChangedScenes(DateTime startTime)
        {
            return GetChangedSceneSourceIds("scene/changed", "scenes", startTime);
        }

        public HashSet<string> GetChangedStudios(DateTime startTime)
        {
            return GetChangedSceneSourceIds("site/changed", "studios", startTime);
        }

        public HashSet<string> GetChangedPerformers(DateTime startTime)
        {
            return GetChangedSceneSourceIds("performer/changed", "performers", startTime);
        }

        // Ids come back in the locked scene source. Any error yields an empty set, and callers fall back to a time-based refresh.
        private HashSet<string> GetChangedSceneSourceIds(string stashRoute, string kind, DateTime startTime)
        {
            // Round down to the hour to ensure we cover gap and don't kill cache every call
            var cacheAdjustedStart = startTime.AddMinutes(-15);
            var startDate = cacheAdjustedStart.Date.AddHours(cacheAdjustedStart.Hour).ToString("s");

            var request = _whisparrMetadata.Create()
                .SetSegment(RouteConst, SceneSourceIds.Route(_configService.WhisparrSceneMetadataSource, stashRoute))
                .AddQueryParam(SinceConst, startDate)
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;

            var response = _httpClient.Get<List<string>>(request);

            if (response.HasHttpError)
            {
                _logger.Warn($"Error fetching changed {kind} from Skyhook: {request.Url} - {response.StatusCode}");
                return new HashSet<string>();
            }

            return new HashSet<string>(response.Resource);
        }

        public Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> GetMovieInfo(int tmdbId)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, "movie")
                                             .Resource(tmdbId.ToString())
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<MovieResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(tmdbId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            return MapMovieInfo(httpResponse.Resource, _configService.WhisparrSceneMetadataSource);
        }

        public Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> GetTpdbMovieInfo(string tpdbId)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, "tpdb/movie")
                                             .Resource(tpdbId)
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<MovieResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(tpdbId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            return MapMovieInfo(httpResponse.Resource, _configService.WhisparrSceneMetadataSource);
        }

        public Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> GetSceneInfo(string stashId)
        {
            return GetSceneInfo(stashId, _configService.WhisparrSceneMetadataSource);
        }

        private Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> GetSceneInfo(string stashId, SceneMetadataType sceneSource)
        {
            var cached = _scenePrefetch.Find(stashId);

            if (cached != null)
            {
                return MapSceneInfo(cached, sceneSource) ?? throw new MovieNotFoundException(stashId);
            }

            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, "scene"))
                                             .Resource(stashId)
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<MovieResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(stashId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            // A scene without an id in the locked source can't be stored, so treat it as missing upstream.
            return MapSceneInfo(httpResponse.Resource, sceneSource) ?? throw new MovieNotFoundException(stashId);
        }

        public ScenePage GetStudioScenePage(string stashId, string cursor)
        {
            return GetScenePage("site", stashId, cursor);
        }

        public ScenePage GetPerformerScenePage(string stashId, string cursor)
        {
            return GetScenePage("performer", stashId, cursor);
        }

        public List<string> PrefetchPerformers(IEnumerable<string> stashIds)
        {
            var sceneSource = _configService.WhisparrSceneMetadataSource;
            var notCached = new List<string>();

            foreach (var batch in stashIds.Where(i => i.IsNotNullOrWhiteSpace()).Distinct().Chunk(PerformerBulkBatchSize))
            {
                try
                {
                    var httpRequest = _whisparrMetadata.Create()
                                                     .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, "performer/bulk"))
                                                     .Build();

                    httpRequest.AllowAutoRedirect = true;
                    httpRequest.SuppressHttpError = true;
                    httpRequest.Headers.ContentType = "application/json";
                    httpRequest.SetContent(batch.ToJson());

                    var httpResponse = _httpClient.Post<PerformerBulkResource>(httpRequest);

                    if (httpResponse.StatusCode != HttpStatusCode.OK && httpResponse.StatusCode != HttpStatusCode.Accepted)
                    {
                        throw new HttpException(httpRequest, httpResponse);
                    }

                    var resource = httpResponse.Resource;
                    var cachedIds = new HashSet<string>();

                    foreach (var performer in resource?.Performers ?? new List<PerformerResource>())
                    {
                        var id = SceneSourceIds.Key(sceneSource, performer?.ForeignIds);

                        if (id == null)
                        {
                            continue;
                        }

                        _performerPrefetch.Set(id, performer, PrefetchLifetime);
                        cachedIds.Add(id);
                    }

                    notCached.AddRange(batch.Where(i => !cachedIds.Contains(i)));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Performer bulk prefetch failed for {0} performers", batch.Length);
                    notCached.AddRange(batch);
                }
            }

            return notCached.Distinct().ToList();
        }

        public void ForgetPrefetched(IEnumerable<string> sceneIds, IEnumerable<string> performerIds)
        {
            foreach (var id in (sceneIds ?? Enumerable.Empty<string>()).Where(i => i.IsNotNullOrWhiteSpace()))
            {
                _scenePrefetch.Remove(id);
            }

            foreach (var id in (performerIds ?? Enumerable.Empty<string>()).Where(i => i.IsNotNullOrWhiteSpace()))
            {
                _performerPrefetch.Remove(id);
            }
        }

        private ScenePage GetScenePage(string route, string stashId, string cursor)
        {
            var sceneSource = _configService.WhisparrSceneMetadataSource;
            var builder = _whisparrMetadata.Create()
                                           .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, route))
                                           .Resource($"{stashId}/scenes/full");

            if (cursor.IsNotNullOrWhiteSpace())
            {
                builder.AddQueryParam("cursor", cursor);
            }

            var httpRequest = builder.Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<ScenePageResource>(httpRequest);

            switch (httpResponse.StatusCode)
            {
                case HttpStatusCode.OK:
                    return MapScenePage(httpResponse, ScenePageStatus.Complete, TimeSpan.Zero, sceneSource);
                case HttpStatusCode.Accepted when httpResponse.Resource is { Building: true }:
                    return new ScenePage
                    {
                        Status = ScenePageStatus.Building,
                        Total = httpResponse.Resource.Total,
                        RetryAfter = ParseRetryAfter(httpResponse, TimeSpan.FromSeconds(5))
                    };
                case HttpStatusCode.Accepted:
                    return MapScenePage(httpResponse, ScenePageStatus.Pending, TimeSpan.FromSeconds(5), sceneSource);
                case HttpStatusCode.ServiceUnavailable:
                    return new ScenePage
                    {
                        Status = ScenePageStatus.Throttled,
                        RetryAfter = ParseRetryAfter(httpResponse, TimeSpan.FromSeconds(30))
                    };
                case HttpStatusCode.NotFound:
                    throw new MovieNotFoundException(stashId);
                default:
                    throw new HttpException(httpRequest, httpResponse);
            }
        }

        private ScenePage MapScenePage(HttpResponse<ScenePageResource> httpResponse, ScenePageStatus status, TimeSpan defaultRetryAfter, SceneMetadataType sceneSource)
        {
            var resource = httpResponse.Resource;
            var items = new List<ScenePageItem>();

            foreach (var scene in resource?.Items ?? new List<MovieResource>())
            {
                var sceneId = SceneSourceIds.Key(sceneSource, scene?.ForeignIds);

                // A scene without an id in the locked source can't be added, so it never reaches the importer.
                if (sceneId == null)
                {
                    continue;
                }

                _scenePrefetch.Set(sceneId, scene, PrefetchLifetime);

                items.Add(new ScenePageItem
                {
                    SceneId = sceneId,
                    Title = scene.Title,
                    ReleaseDate = scene.ReleaseDateUtc,
                    PerformerIds = (scene.Credits ?? new List<CastResource>())
                        .Select(c => SceneSourceIds.Key(sceneSource, c?.Performer?.ForeignIds))
                        .Where(i => i != null)
                        .Distinct()
                        .ToList()
                });
            }

            return new ScenePage
            {
                Status = status,
                Total = resource?.Total ?? 0,
                NextCursor = resource?.NextCursor,
                Items = items,
                Pending = resource?.Pending ?? new List<string>(),
                RetryAfter = status == ScenePageStatus.Pending ? ParseRetryAfter(httpResponse, defaultRetryAfter) : defaultRetryAfter
            };
        }

        private static TimeSpan ParseRetryAfter(HttpResponse response, TimeSpan fallback)
        {
            if (!response.Headers.ContainsKey("Retry-After"))
            {
                return fallback;
            }

            var retryAfter = response.Headers["Retry-After"].ToString();

            if (int.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return ClampRetryAfter(TimeSpan.FromSeconds(seconds));
            }

            if (DateTime.TryParseExact(retryAfter, "r", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
            {
                return ClampRetryAfter(date - DateTime.UtcNow);
            }

            return fallback;
        }

        // A bad header must neither spin the walker nor stall it for hours.
        private static TimeSpan ClampRetryAfter(TimeSpan value)
        {
            return TimeSpan.FromTicks(Math.Clamp(value.Ticks, MinRetryAfter.Ticks, MaxRetryAfter.Ticks));
        }

        private static Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> MapMovieInfo(MovieResource resource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            var movie = MapMovie(resource, sceneSource) ?? throw new MovieNotFoundException(resource.Title);

            var credits = resource.Credits.Select(c => MapCast(c, sceneSource)).Where(c => c != null).ToList();

            var performers = MapCreditPerformers(resource.Credits, sceneSource);

            movie.PerformerForeignIds = performers.Select(p => p.ForeignId).ToList();
            movie.PerformerNames = credits.Select(p => p.PersonName).ToList();

            return new Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(movie, MapStudio(resource.Studio, sceneSource), performers, credits);
        }

        // Returns null when the scene has no id in the locked scene source.
        private static Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> MapSceneInfo(MovieResource resource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            var movie = MapMovie(resource, sceneSource);

            if (movie == null)
            {
                return null;
            }

            var credits = resource.Credits.Select(c => MapSceneCast(c, sceneSource)).Where(c => c != null).ToList();

            var performers = MapCreditPerformers(resource.Credits, sceneSource);

            movie.PerformerForeignIds = performers.Select(p => p.ForeignId).ToList();
            movie.PerformerNames = credits.Select(p => p.PersonName).ToList();

            return new Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(movie, MapStudio(resource.Studio, sceneSource), performers, credits);
        }

        private static List<Performer> MapCreditPerformers(IEnumerable<CastResource> credits, SceneMetadataType sceneSource)
        {
            return credits.Select(c => c.Performer == null ? null : MapPerformer(c.Performer, sceneSource))
                .Where(p => p != null)
                .DistinctBy(p => p.ForeignId)
                .ToList();
        }

        public MovieCollection GetCollectionInfo(int tmdbId)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, "movie/collection")
                                             .Resource(tmdbId.ToString())
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<CollectionResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(tmdbId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var collection = MapCollection(httpResponse.Resource, _configService.WhisparrSceneMetadataSource);

            return collection;
        }

        public List<MovieMetadata> GetBulkMovieInfo(List<int> tmdbIds)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, "movie/bulk")
                                             .Build();

            httpRequest.Headers.ContentType = "application/json";

            httpRequest.SetContent(tmdbIds.ToJson());
            httpRequest.ContentSummary = tmdbIds.ToJson(Formatting.None);

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Post<List<MovieResource>>(httpRequest);

            if (httpResponse.HasHttpError || httpResponse.Resource.Count == 0)
            {
                throw new HttpException(httpRequest, httpResponse);
            }

            var sceneSource = _configService.WhisparrSceneMetadataSource;
            var movies = httpResponse.Resource.Select(r => MapMovie(r, sceneSource)).Where(m => m != null).ToList();

            return movies;
        }

        public List<MovieMetadata> GetBulkTpdbMovieInfo(List<string> tpdbIds)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, "tpdb/movie/bulk")
                                             .Build();

            httpRequest.Headers.ContentType = "application/json";

            httpRequest.SetContent(tpdbIds.ToJson());
            httpRequest.ContentSummary = tpdbIds.ToJson(Formatting.None);

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Post<List<MovieResource>>(httpRequest);

            if (httpResponse.HasHttpError || httpResponse.Resource.Count == 0)
            {
                throw new HttpException(httpRequest, httpResponse);
            }

            var sceneSource = _configService.WhisparrSceneMetadataSource;
            var movies = httpResponse.Resource.Select(r => MapMovie(r, sceneSource)).Where(m => m != null).ToList();

            return movies;
        }

        public MovieMetadata GetMovieByImdbId(string imdbId)
        {
            imdbId = Parser.Parser.NormalizeImdbId(imdbId);

            if (imdbId == null)
            {
                return null;
            }

            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, "movie/imdb")
                                             .Resource(imdbId.ToString())
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<List<MovieResource>>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(imdbId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            var sceneSource = _configService.WhisparrSceneMetadataSource;
            var movie = httpResponse.Resource.Select(r => MapMovie(r, sceneSource)).FirstOrDefault(m => m != null);

            return movie;
        }

        public Performer GetPerformerInfo(string stashId)
        {
            return GetPerformerInfo(stashId, _configService.WhisparrSceneMetadataSource);
        }

        private Performer GetPerformerInfo(string stashId, SceneMetadataType sceneSource)
        {
            var cached = _performerPrefetch.Find(stashId);

            if (cached != null)
            {
                return MapPerformer(cached, sceneSource) ?? throw new MovieNotFoundException(stashId);
            }

            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, "performer"))
                                             .Resource(stashId)
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<PerformerResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(stashId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            // A performer without an id in the locked source can't be stored, so treat it as missing upstream.
            return MapPerformer(httpResponse.Resource, sceneSource) ?? throw new MovieNotFoundException(stashId);
        }

        public Studio GetStudioInfo(string stashId)
        {
            return GetStudioInfo(stashId, _configService.WhisparrSceneMetadataSource);
        }

        private Studio GetStudioInfo(string stashId, SceneMetadataType sceneSource)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, "site"))
                                             .Resource(stashId)
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<StudioResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(stashId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            return MapStudio(httpResponse.Resource, sceneSource);
        }

        public (List<string> StashdbIds, List<string> TpdbIds, List<int> TmdbIds) GetPerformerWorks(string stashId)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, SceneSourceIds.Route(_configService.WhisparrSceneMetadataSource, "performer"))
                                             .Resource($"{stashId}/works")
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<PerformerWorksResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(stashId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            // TPDB works fill TpdbMovies only; the other lists may be missing.
            var stashIds = httpResponse.Resource.Scenes ?? new List<string>();
            var tpdbIds = httpResponse.Resource.TpdbMovies ?? new List<string>();
            var tmdbIds = httpResponse.Resource.Movies != null
                ? httpResponse.Resource.Movies.ConvertAll(int.Parse)
                : new List<int>();

            return (stashIds, tpdbIds, tmdbIds);
        }

        public List<string> GetStudioScenes(string stashId)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, "site")
                                             .Resource($"{stashId}/scenes")
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<List<string>>(httpRequest);
            var scenes = httpResponse.Resource;

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(stashId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            return scenes;
        }

        public (List<string> StashdbIds, List<string> TpdbIds, List<int> TmdbIds) GetStudioWorks(string stashId)
        {
            var httpRequest = _whisparrMetadata.Create()
                                             .SetSegment(RouteConst, SceneSourceIds.Route(_configService.WhisparrSceneMetadataSource, "site"))
                                             .Resource($"{stashId}/works")
                                             .Build();

            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<StudioWorksResource>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MovieNotFoundException(stashId);
                }
                else
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            // TPDB works fill TpdbMovies only; the other lists may be missing.
            var stashdbIds = httpResponse.Resource.Scenes ?? new List<string>();
            var tpdbIds = httpResponse.Resource.TpdbMovies ?? new List<string>();
            var tmdbIds = httpResponse.Resource.Movies != null
                ? httpResponse.Resource.Movies.ConvertAll(int.Parse)
                : new List<int>();

            return (stashdbIds, tpdbIds, tmdbIds);
        }

        public MovieMetadata MapMovieToTmdbMovie(MovieMetadata movie)
        {
            if (movie == null)
            {
                throw new ArgumentNullException(nameof(movie));
            }

            try
            {
                MovieMetadata newMovie;
                var sceneSource = _configService.WhisparrSceneMetadataSource;
                var sceneKey = SceneKey(movie, sceneSource);

                if (movie.TmdbId > 0)
                {
                    newMovie = _movieMetadataService.FindByTmdbId(movie.TmdbId);

                    if (newMovie != null)
                    {
                        return newMovie;
                    }

                    newMovie = GetMovieInfo(movie.TmdbId).Item1;
                }
                else if (sceneKey.IsNotNullOrWhiteSpace())
                {
                    newMovie = _movieMetadataService.FindByForeignId(sceneKey);

                    if (newMovie != null)
                    {
                        return newMovie;
                    }

                    newMovie = GetSceneInfo(sceneKey, sceneSource).Item1;
                }
                else if (movie.ImdbId.IsNotNullOrWhiteSpace())
                {
                    newMovie = _movieMetadataService.FindByImdbId(Parser.Parser.NormalizeImdbId(movie.ImdbId));

                    if (newMovie != null)
                    {
                        return newMovie;
                    }

                    newMovie = GetMovieByImdbId(movie.ImdbId);
                }
                else
                {
                    var yearStr = "";
                    if (movie.Year > 1900)
                    {
                        yearStr = $" {movie.Year}";
                    }

                    var newMovieObject = SearchForNewMovie(movie.Title + yearStr).FirstOrDefault();

                    if (newMovieObject == null)
                    {
                        newMovie = null;
                    }
                    else
                    {
                        newMovie = newMovieObject.MovieMetadata;
                    }
                }

                if (newMovie == null)
                {
                    _logger.Warn("Couldn't map movie {0} to a movie on The Movie DB. It will not be added :(", movie.Title);
                    return null;
                }

                return newMovie;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't map movie {0} to a movie on The Movie DB. It will not be added :(", movie.Title);
                return null;
            }
        }

        private static string SceneKey(MovieMetadata movie, SceneMetadataType sceneSource)
        {
            if (sceneSource != SceneMetadataType.Tpdb)
            {
                return movie.StashId;
            }

            // Under TPDB a movie carries a TPDB id too, so only a scene's id is a scene key.
            return movie.ItemType == ItemType.Scene ? movie.TpdbId : null;
        }

        public List<object> SearchForNewEntity(string title, ItemType? itemType = null)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<object>();
            }

            if (title.IsPathValid(PathValidationType.AnyOs))
            {
                throw new InvalidSearchTermException("Invalid search term '{0}'", title);
            }

            var result = new List<object>();
            var movieInfo = Parser.Parser.ParseMoviePath(title);

            // Null is passed when a Filename is passed where the type is not set
            if (itemType == null)
            {
                if (movieInfo != null)
                {
                    itemType = movieInfo.IsScene ? ItemType.Scene : ItemType.Movie;
                }
                else
                {
                    itemType = ItemType.Movie;
                    _logger.Debug($"Unable to parse title {title} to determine if it's a movie or scene, defaulting to movie search");
                }
            }

            if (movieInfo is { IsScene: true } && itemType != ItemType.Movie &&
                movieInfo.StashId.IsNullOrWhiteSpace() &&
                movieInfo.StudioTitle.IsNotNullOrWhiteSpace() &&
                movieInfo.ReleaseDate.IsNotNullOrWhiteSpace())
            {
                return SearchForNewSceneByStudioAndDate(movieInfo).Cast<object>().ToList();
            }

            title = FormatSearchTerm(title, itemType, movieInfo);
            var lowerTitle = title.ToLowerInvariant();

            if (lowerTitle.StartsWith("imdb:", StringComparison.Ordinal) || lowerTitle.StartsWith("imdbid:", StringComparison.Ordinal))
            {
                throw new InvalidSearchTermException("IMDb ids are not supported by Whisparr metadata. Search by title or use a TMDb/TPDB id instead");
            }

            if (lowerTitle.StartsWith(TmdbPrefix, StringComparison.Ordinal) || lowerTitle.StartsWith(TmdbIdPrefix, StringComparison.Ordinal) || lowerTitle.StartsWith(TpdbPrefix, StringComparison.Ordinal) || lowerTitle.StartsWith(TpdbIdPrefix, StringComparison.Ordinal) || lowerTitle.StartsWith(StashPrefixConst, StringComparison.Ordinal) || lowerTitle.StartsWith(StashIdPrefixConst, StringComparison.Ordinal) || lowerTitle.StartsWith("https:", StringComparison.Ordinal))
            {
                _logger.Debug($"Searching for {itemType.ToString()} - {title}");
                var movies = itemType == ItemType.Movie ? SearchForNewMovie(title) : SearchForNewScene(title);

                foreach (var movie in movies)
                {
                    result.Add(movie);
                }
            }
            else
            {
                var searchTerm = ReplaceSearchSeparators(lowerTitle);
                var movies = itemType == ItemType.Movie ? SearchForNewMovie(searchTerm) : SearchForNewScene(searchTerm);

                foreach (var movie in movies)
                {
                    result.Add(movie);
                }
            }

            return result;
        }

        public List<Movie> SearchForNewMovie(string title)
        {
            var results = new List<Movie>();

            if (_configService.WhisparrMovieMetadataSource == MovieMetadataType.NONE)
            {
                return results;
            }

            if (_configService.WhisparrMovieMetadataSource == MovieMetadataType.TMDB)
            {
                results.AddRange(SearchForNewTmdbMovie(title));
            }

            if (_configService.WhisparrMovieMetadataSource == MovieMetadataType.TPDB)
            {
                results.AddRange(SearchForNewTpdbMovie(title));
            }

            return results;
        }

        public List<Movie> SearchForNewTmdbMovie(string title)
        {
            var normalizedTitle = title.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal);

            if (normalizedTitle.StartsWith(TpdbPrefix, StringComparison.Ordinal) || normalizedTitle.StartsWith(TpdbIdPrefix, StringComparison.Ordinal))
            {
                throw new InvalidSearchTermException("TPDB ids can't be searched while the movie metadata source is TMDb. Change it in General Settings or search by title instead");
            }

            try
            {
                var regex = new Regex("^https://www.themoviedb.org/movie/(?<tmdbid>[0-9]+).*$", RegexOptions.None, RegexDefaults.Timeout);
                var match = regex.Match(title);

                if (match.Success)
                {
                    title = TmdbPrefix + match.Groups["tmdbid"].Value;
                }

                var lowerTitle = title.ToLowerInvariant();

                lowerTitle = lowerTitle.Replace(".", "", StringComparison.Ordinal);

                var parserTitle = lowerTitle;

                var parserResult = Parser.Parser.ParseMovieTitle(title, true);

                var yearTerm = "";

                if (parserResult != null && parserResult.PrimaryMovieTitle != title)
                {
                    // Parser found something interesting!
                    if (parserResult.PrimaryMovieTitle.IsNotNullOrWhiteSpace())
                    {
                        parserTitle = parserResult.PrimaryMovieTitle.ToLowerInvariant().Replace(".", " "); // TODO Update so not every period gets replaced (e.g. R.I.P.D.)
                    }

                    if (parserResult.Year > 1800)
                    {
                        yearTerm = parserResult.Year.ToString();
                    }

                    if (parserResult.TmdbId > 0)
                    {
                        try
                        {
                            var movieLookup = GetMovieInfo(parserResult.TmdbId).Item1;
                            return movieLookup == null ? new List<Movie>() : new List<Movie> { _movieService.FindByTmdbId(movieLookup.TmdbId) ?? new Movie { MovieMetadata = movieLookup } };
                        }
                        catch (Exception)
                        {
                            return new List<Movie>();
                        }
                    }
                }

                parserTitle = StripTrailingTheFromTitle(parserTitle);

                if (lowerTitle.StartsWith(TmdbPrefix, StringComparison.Ordinal) || lowerTitle.StartsWith(TmdbIdPrefix, StringComparison.Ordinal))
                {
                    var slug = lowerTitle.Split(':')[1].Trim();

                    var tmdbid = -1;

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace) || !int.TryParse(slug, out tmdbid))
                    {
                        return new List<Movie>();
                    }

                    try
                    {
                        var movieLookup = GetMovieInfo(tmdbid).Item1;
                        return movieLookup == null ? new List<Movie>() : new List<Movie> { _movieService.FindByTmdbId(movieLookup.TmdbId) ?? new Movie { MovieMetadata = movieLookup } };
                    }
                    catch (MovieNotFoundException)
                    {
                        return new List<Movie>();
                    }
                }

                var searchTerm = ReplaceSearchSeparators(parserTitle);

                var request = _whisparrMetadata.Create()
                    .SetSegment(RouteConst, "movie/search")
                    .AddQueryParam("q", searchTerm)
                    .AddQueryParam("year", yearTerm)
                    .Build();

                request.AllowAutoRedirect = true;

                var httpResponse = _httpClient.Get<List<MovieResource>>(request);
                var sceneSource = _configService.WhisparrSceneMetadataSource;

                return httpResponse.Resource
                    .Select(resource =>
                    {
                        try
                        {
                            return MapSearchResult(resource, sceneSource);
                        }
                        catch (Exception ex)
                        {
                            _logger.Trace(ex, $"Failed to map search result for resource: {resource?.ToJson(Formatting.None)}");
                            return null;
                        }
                    })
                    .Where(x => x != null)
                    .ToList();
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookConnectionExceptionConst, ex, title, ex.Message);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookConnectionExceptionConst, ex, title, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Invalid response received from WhisparrAPI. {1}", ex, title, ex.Message);
            }
        }

        public List<Movie> SearchForNewTpdbMovie(string title)
        {
            var normalizedTitle = title.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal);

            if (normalizedTitle.StartsWith(TmdbPrefix, StringComparison.Ordinal) || normalizedTitle.StartsWith(TmdbIdPrefix, StringComparison.Ordinal))
            {
                throw new InvalidSearchTermException("TMDb ids can't be searched while the movie metadata source is TPDB. Change it in General Settings or search by title instead");
            }

            try
            {
                var lowerTitle = title.ToLowerInvariant();

                lowerTitle = lowerTitle.Replace(".", "", StringComparison.Ordinal);

                // Allow search to accept a full StashDB URL
                var regex = new Regex(@"^https://theporndb\.net/movies/(?<tpdbid>[A-Za-z0-9\-]+).*$", RegexOptions.Compiled, RegexDefaults.Timeout);
                var match = regex.Match(title);

                if (match.Success)
                {
                    lowerTitle = TpdbIdPrefix + match.Groups["tpdbid"].Value;
                    _logger.Debug($"Search based on TPDB URL.  Re-writing as {lowerTitle}");
                }

                if (lowerTitle.StartsWith(TpdbPrefix, StringComparison.Ordinal) || lowerTitle.StartsWith("tpdbid:", StringComparison.Ordinal))
                {
                    var slug = lowerTitle.Split(':')[1].Trim();

                    var tpdbid = slug;

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                    {
                        return new List<Movie>();
                    }

                    try
                    {
                        var movieLookup = GetTpdbMovieInfo(tpdbid).Item1;
                        return movieLookup == null ? new List<Movie>() : new List<Movie> { _movieService.FindByTpdbId(movieLookup.TpdbId) ?? new Movie { MovieMetadata = movieLookup } };
                    }
                    catch (MovieNotFoundException ex)
                    {
                        _logger.Debug(ex, $"Movie not found");
                        return new List<Movie>();
                    }
                }

                var parserTitle = lowerTitle;

                var parserResult = Parser.Parser.ParseMovieTitle(title, true);

                var yearTerm = "";

                if (parserResult != null && parserResult.PrimaryMovieTitle.IsNotNullOrWhiteSpace() && parserResult.PrimaryMovieTitle != title)
                {
                    // Parser found something interesting!
                    parserTitle = parserResult.PrimaryMovieTitle.ToLowerInvariant().Replace(".", " ", StringComparison.Ordinal); // TODO Update so not every period gets replaced (e.g. R.I.P.D.)
                    if (parserResult.Year > 1800)
                    {
                        yearTerm = parserResult.Year.ToString();
                    }
                }

                parserTitle = StripTrailingTheFromTitle(parserTitle);

                var searchTerm = ReplaceSearchSeparators(parserTitle);

                var request = _whisparrMetadata.Create()
                    .SetSegment(RouteConst, "tpdb/movie/search")
                    .AddQueryParam("q", searchTerm)
                    .AddQueryParam("year", yearTerm)
                    .Build();

                request.AllowAutoRedirect = true;

                var httpResponse = _httpClient.Get<List<MovieResource>>(request);
                var sceneSource = _configService.WhisparrSceneMetadataSource;

                return httpResponse.Resource
                    .Select(resource =>
                    {
                        try
                        {
                            return MapSearchResult(resource, sceneSource);
                        }
                        catch (Exception ex)
                        {
                            _logger.Trace(ex, $"Failed to map TPDB search result for resource: {resource?.ToJson(Formatting.None)}");
                            return null;
                        }
                    })
                    .Where(x => x != null)
                    .ToList();
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookConnectionExceptionConst, ex, title, ex.Message);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookConnectionExceptionConst, ex, title, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Invalid response received from WhisparrAPI. {1}", ex, title, ex.Message);
            }
        }

        public List<Movie> SearchForNewScene(string title)
        {
            return SearchForNewScene(title, null, null, _configService.WhisparrSceneMetadataSource);
        }

        // True when the term names an id rather than a title; id is null when that id can't be looked up.
        // Under TPDB a bare UUID, or one behind any stash/tpdb prefix, is looked up directly.
        private static bool TryGetLookupId(string lowerTitle, SceneMetadataType sceneSource, out string id)
        {
            if (sceneSource == SceneMetadataType.Tpdb)
            {
                var term = lowerTitle.Trim();
                var prefix = TpdbLookupPrefixes.FirstOrDefault(p => term.StartsWith(p, StringComparison.Ordinal));
                var candidate = prefix == null ? term : term.Substring(prefix.Length).Trim();

                id = Guid.TryParseExact(candidate, "D", out _) ? candidate : null;

                return prefix != null || id != null;
            }

            if (lowerTitle.StartsWith(StashPrefixConst, StringComparison.Ordinal) || lowerTitle.StartsWith(StashIdPrefixConst, StringComparison.Ordinal))
            {
                var slug = lowerTitle.Split(':')[1].Trim();

                id = slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace) ? null : slug;

                return true;
            }

            id = null;

            return false;
        }

        public List<Performer> SearchForNewPerformer(string title)
        {
            try
            {
                var sceneSource = _configService.WhisparrSceneMetadataSource;
                var lowerTitle = title.ToLowerInvariant();

                lowerTitle = lowerTitle.Replace(".", "", StringComparison.Ordinal);

                // Allow search to accept a full StashDB URL
                var regex = new Regex(@"^https://stashdb\.org/performers/(?<stashid>[A-Za-z0-9\-]+).*$", RegexOptions.Compiled, RegexDefaults.Timeout);
                var match = regex.Match(title);

                if (match.Success)
                {
                    lowerTitle = StashPrefixConst + match.Groups["stashid"].Value;
                    _logger.Debug($"Search based on StashDB URL.  Re-writing as {lowerTitle}");
                }

                if (TryGetLookupId(lowerTitle, sceneSource, out var performerId))
                {
                    if (performerId == null)
                    {
                        return new List<Performer>();
                    }

                    try
                    {
                        var performerLookup = GetPerformerInfo(performerId, sceneSource);
                        return performerLookup == null ? new List<Performer>() : new List<Performer>() { performerLookup };
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, $"Performer not found");
                        return new List<Performer>();
                    }
                }

                var searchTerm = ReplaceSearchSeparators(lowerTitle);

                var request = _whisparrMetadata.Create()
                    .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, "performer/search"))
                    .AddQueryParam("q", searchTerm)
                    .Build();

                request.AllowAutoRedirect = true;

                var httpResponse = _httpClient.Get<List<PerformerResource>>(request);

                return httpResponse.Resource.Select(p => MapPerformer(p, sceneSource)).Where(p => p != null).ToList();
            }
            catch (UnexpectedHtmlContentException ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed. StashDb returned a HTML Response.", title.ForLog());
                return new List<Performer>();
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookExceptionMessageConst, ex, title);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookExceptionMessageConst, ex, title);
            }
            catch (JsonException ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed. StashDb returned a JSON response.", title.ForLog());
                return new List<Performer>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Invalid response received from StashDb.", ex, title);
            }
        }

        public List<Studio> SearchForNewStudio(string title)
        {
            return SearchForNewStudio(title, _configService.WhisparrSceneMetadataSource);
        }

        private List<Studio> SearchForNewStudio(string title, SceneMetadataType sceneSource)
        {
            try
            {
                var lowerTitle = title.ToLowerInvariant();

                lowerTitle = lowerTitle.Replace(".", "", StringComparison.Ordinal);

                // Allow search to accept a full StashDB URL
                var regex = new Regex(@"^https://stashdb\.org/studios/(?<stashid>[A-Za-z0-9\-]+).*$", RegexOptions.Compiled, RegexDefaults.Timeout);
                var match = regex.Match(title);

                if (match.Success)
                {
                    lowerTitle = StashPrefixConst + match.Groups["stashid"].Value;
                    _logger.Debug($"Search based on StashDB URL.  Re-writing as {lowerTitle}");
                }

                if (TryGetLookupId(lowerTitle, sceneSource, out var studioId))
                {
                    if (studioId == null)
                    {
                        return new List<Studio>();
                    }

                    try
                    {
                        var studioLookup = GetStudioInfo(studioId, sceneSource);
                        return studioLookup == null ? new List<Studio>() : new List<Studio>() { studioLookup };
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, $"Studio not found");
                        return new List<Studio>();
                    }
                }

                var searchTerm = ReplaceSearchSeparators(lowerTitle);

                var request = _whisparrMetadata.Create()
                    .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, "site/search"))
                    .AddQueryParam("q", searchTerm)
                    .Build();

                request.AllowAutoRedirect = true;

                var httpResponse = _httpClient.Get<List<StudioResource>>(request);

                return httpResponse.Resource.Select(s => MapStudio(s, sceneSource)).Where(s => s != null).ToList();
            }
            catch (UnexpectedHtmlContentException ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed. StashDb returned a HTML Response.", title.ForLog());
                return new List<Studio>();
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookExceptionMessageConst, ex, title);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookExceptionMessageConst, ex, title);
            }
            catch (JsonException ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed. StashDb returned a JSON response.", title.ForLog());
                return new List<Studio>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Invalid response received from StashDb.", ex, title);
            }
        }

        private static Credit MapCast(CastResource castResource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            if (castResource == null)
            {
                throw new ArgumentNullException(nameof(castResource));
            }

            var performerKey = SceneSourceIds.Key(sceneSource, castResource.Performer?.ForeignIds);

            if (performerKey == null)
            {
                return null;
            }

            var personName = castResource.PersonName ?? castResource.Performer.Name;

            if (personName == null)
            {
                personName = "";
            }

            var newActor = new Credit
            {
                PersonName = personName,
                Character = castResource.Character,
                Order = castResource.Order,
                Type = CreditType.Cast,
                Images = castResource.Performer.Images.Select(MapImage).ToList(),
                PerformerForeignId = performerKey,

                // To Remove once client uses the Credit Controller
                Performer = new CreditPerformer
                {
                    Name = castResource.Performer.Name,
                    Disambiguation = castResource.Performer?.Disambiguation ?? string.Empty,
                    ForeignId = performerKey,
                    Images = castResource.Performer.Images.Select(MapImage).ToList(),
                    Gender = MapGender(castResource.Performer?.Gender),
                    Country = castResource.Performer?.Country ?? string.Empty
                }
            };

            return newActor;
        }

        private static Gender MapGender(string gender)
        {
            if (gender.IsNullOrWhiteSpace())
            {
                return Gender.Female;
            }

            switch (gender.ToUpperInvariant())
            {
                case "TRANSGENDER_FEMALE":
                    return Gender.TransFemale;
                case "TRANSGENDER_MALE":
                    return Gender.TransMale;
                case "NON_BINARY":
                    return Gender.NonBinary;
                case "INTERSEX":
                    return Gender.Intersex;
                case "MALE":
                    return Gender.Male;
                default:
                    return Gender.Female;
            }
        }

        private static string FormatSearchTerm(string title, ItemType? itemType, ParsedMovieInfo movieInfo)
        {
            if (movieInfo == null)
            {
                var path = Path.GetFileNameWithoutExtension(title);
                if (path.IsNotNullOrWhiteSpace() && path != title)
                {
                    title = path;
                }

                return title;
            }

            if (movieInfo.IsScene && itemType != ItemType.Movie)
            {
                if (movieInfo.StashId.IsNotNullOrWhiteSpace())
                {
                    return $"stashid:{movieInfo.StashId}";
                }
            }
            else if (!movieInfo.IsScene && itemType != ItemType.Scene && movieInfo.TmdbId > 0)
            {
                return $"tmdb:{movieInfo.TmdbId}";
            }

            return title;
        }

        private static Credit MapSceneCast(CastResource castResource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            ArgumentNullException.ThrowIfNull(castResource);

            var performerKey = SceneSourceIds.Key(sceneSource, castResource.Performer?.ForeignIds);

            if (performerKey == null)
            {
                return null;
            }

            // Extract the person name from older skyhook data
            var personName = castResource.PersonName.IsNotNullOrWhiteSpace() ? castResource.PersonName : castResource.Performer.Name;

            if (personName == null)
            {
                personName = "";
            }

            var newActor = new Credit
            {
                PersonName = personName,
                Character = castResource.Character,
                Order = castResource.Order,
                Type = CreditType.Cast,
                Images = castResource.Performer.Images.Select(MapImage).ToList(),
                PerformerForeignId = performerKey,

                // To Remove once client uses the Credit Controller
                Performer = new CreditPerformer
                {
                    Name = castResource.Performer.Name,
                    ForeignId = performerKey,
                    Images = castResource.Performer.Images?.Select(MapImage).ToList() ?? new List<MediaCover.MediaCover>(),
                    Gender = MapGender(castResource.Performer.Gender)
                }
            };

            return newActor;
        }

        private static AlternativeTitle MapAlternativeTitle(AlternativeTitleResource altTitleResource)
        {
            if (altTitleResource == null)
            {
                throw new ArgumentNullException(nameof(altTitleResource));
            }

            var newAlternativeTitle = new AlternativeTitle
            {
                Title = altTitleResource.Title,
                SourceType = altTitleResource.Type,
                CleanTitle = altTitleResource.Title.CleanMovieTitle()
            };

            return newAlternativeTitle;
        }

        private static EyeColor? MapEyeColor(string eyeColor)
        {
            if (eyeColor.IsNullOrWhiteSpace())
            {
                return null;
            }

            switch (eyeColor.ToUpperInvariant())
            {
                case "BROWN":
                    return EyeColor.Brown;
                case "HAZEL":
                    return EyeColor.Hazel;
                case "BLUE":
                    return EyeColor.Blue;
                case "GREEN":
                    return EyeColor.Green;
                case "GREY":
                    return EyeColor.Grey;
                case "RED":
                    return EyeColor.Red;
                default:
                    return EyeColor.Other;
            }
        }

        private static HairColor? MapHairColor(string hairColor)
        {
            if (hairColor.IsNullOrWhiteSpace())
            {
                return null;
            }

            switch (hairColor.ToUpperInvariant())
            {
                case "BLONDE":
                    return HairColor.Blonde;
                case "BLACK":
                    return HairColor.Black;
                case "RED":
                    return HairColor.Red;
                case "AUBURN":
                    return HairColor.Auburn;
                case "GREY":
                    return HairColor.Grey;
                case "VARIOUS":
                    return HairColor.Various;
                case "BALD":
                    return HairColor.Bald;
                default:
                    return HairColor.Other;
            }
        }

        private static Ethnicity? MapEthnicity(string ethnicity)
        {
            if (ethnicity.IsNullOrWhiteSpace())
            {
                return null;
            }

            switch (ethnicity.ToUpperInvariant())
            {
                case "CAUCASIAN":
                    return Ethnicity.Caucasian;
                case "BLACK":
                    return Ethnicity.Black;
                case "ASIAN":
                    return Ethnicity.Asian;
                case "INDIAN":
                    return Ethnicity.Indian;
                case "MIDDLE_EASTERN":
                    return Ethnicity.MiddleEastern;
                case "LATIN":
                    return Ethnicity.Latin;
                case "MIXED":
                    return Ethnicity.Mixed;
                default:
                    return Ethnicity.Other;
            }
        }

        private static Ratings MapRatings(RatingResource ratings)
        {
            if (ratings == null)
            {
                return new Ratings();
            }

            var mappedRatings = new Ratings();

            if (ratings.Tmdb != null)
            {
                mappedRatings.Tmdb = new RatingChild
                {
                    Type = RatingType.User,
                    Value = ratings.Tmdb.Value,
                    Votes = ratings.Tmdb.Count
                };
            }

            return mappedRatings;
        }

        private static MovieCollection MapCollection(CollectionResource collectionResource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            if (collectionResource == null)
            {
                throw new ArgumentNullException(nameof(collectionResource));
            }

            var collection = new MovieCollection
            {
                TmdbId = collectionResource.ForeignIds.TmdbId,
                Title = collectionResource.Name,
                Overview = collectionResource.Overview,
                CleanTitle = collectionResource.Name.CleanMovieTitle(),
                SortTitle = Parser.Parser.NormalizeTitle(collectionResource.Name),
                Images = collectionResource.Images?.Select(MapImage).ToList() ?? new List<MediaCover.MediaCover>(),
                Movies = collectionResource.Parts?.Select(x => MapMovie(x, sceneSource)).Where(m => m != null).ToList() ?? new List<MovieMetadata>()
            };

            return collection;
        }

        private static MediaCover.MediaCover MapImage(ImageResource imageResource)
        {
            if (imageResource == null)
            {
                throw new ArgumentNullException(nameof(imageResource));
            }

            return new MediaCover.MediaCover
            {
                RemoteUrl = imageResource.Url,
                CoverType = MapCoverType(imageResource.CoverType)
            };
        }

        private static MediaCoverTypes MapCoverType(string coverType)
        {
            switch (coverType.ToLowerInvariant())
            {
                case "poster":
                    return MediaCoverTypes.Poster;
                case "headshot":
                    return MediaCoverTypes.Headshot;
                case "fanart":
                    return MediaCoverTypes.Fanart;
                case "clearlogo":
                    return MediaCoverTypes.Clearlogo;
                case "screenshot":
                    return MediaCoverTypes.Screenshot;
                default:
                    return MediaCoverTypes.Unknown;
            }
        }

        private static string StripTrailingTheFromTitle(string title)
        {
            if (title.EndsWith(",the", StringComparison.Ordinal))
            {
                title = title.Substring(0, title.Length - 4);
            }
            else if (title.EndsWith(", the", StringComparison.Ordinal))
            {
                title = title.Substring(0, title.Length - 5);
            }

            return title;
        }

        private static string ReplaceSearchSeparators(string term)
        {
            return term.Replace("_", " ", StringComparison.Ordinal).Replace(".", " ", StringComparison.Ordinal);
        }

        /// <summary>Resolves a studio parsed from a release to a metadata server studio.</summary>
        /// <param name="studioTitle">The studio name as it was parsed from the release.</param>
        /// <param name="cleanStudioTitle">The cleaned form of that name, used to confirm the match.</param>
        /// <param name="sceneSource">The locked scene source, which picks the studio search route.</param>
        /// <remarks>
        /// A name search answers with near misses as readily as with the studio asked for, so only a
        /// result whose own name cleans down to the same value is accepted. Binding to the wrong studio
        /// would hand the scene search a studio id that quietly matches somebody else's scene.
        /// </remarks>
        /// <returns>The matching studio, or null when the name cannot be resolved.</returns>
        private Studio FindStudioByTitle(string studioTitle, string cleanStudioTitle, SceneMetadataType sceneSource)
        {
            var searchTitle = studioTitle.ExpandStudioTitle();

            try
            {
                var studio = SearchForNewStudio(searchTitle, sceneSource)
                    .FirstOrDefault(s => s != null &&
                                         s.Title.CleanStudioTitle() == cleanStudioTitle &&
                                         Guid.TryParse(s.ForeignId, out _));

                if (studio == null)
                {
                    _logger.Debug("No studio matching '{0}' found for '{1}'", searchTitle, studioTitle);
                }
                else
                {
                    _logger.Debug("Resolved studio '{0}' to {1} ({2})", studioTitle, studio.Title, studio.ForeignId);
                }

                return studio;
            }
            catch (SkyHookException ex)
            {
                _logger.Debug(ex, "Studio search for '{0}' failed", searchTitle);
                return null;
            }
        }

        private List<Movie> SearchForNewSceneByStudioAndDate(ParsedMovieInfo movieInfo)
        {
            var sceneSource = _configService.WhisparrSceneMetadataSource;
            var releaseDate = movieInfo.ReleaseDate;
            var cleanStudioTitle = movieInfo.StudioTitle.CleanStudioTitle();

            // A studio and date search must never return another studio's scene, and only a search by
            // studio id can promise that. A studio already in the library carries its id; anything else
            // is resolved against the metadata server before the scene search is made.
            var studio = _studioService.FindByTitle(movieInfo.StudioTitle);

            if (studio == null || !Guid.TryParse(studio.ForeignId, out _))
            {
                studio = FindStudioByTitle(movieInfo.StudioTitle, cleanStudioTitle, sceneSource);
            }

            List<Movie> results;
            Func<MovieMetadata, bool> isSameStudio;

            // A release writes the studio as one run-together token, which matches nothing, so an unknown
            // studio is sent with its word breaks restored.
            var studioTitle = studio?.Title ?? movieInfo.StudioTitle.ExpandStudioTitle();

            if (studio != null)
            {
                isSameStudio = m => string.Equals(m.StudioForeignId, studio.ForeignId, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                isSameStudio = m => m.StudioTitle.CleanStudioTitle() == cleanStudioTitle;
            }

            if (sceneSource == SceneMetadataType.Tpdb)
            {
                // The TPDB route takes q only, so the studio, date and title go in one string.
                var sceneTitle = ReplaceSearchSeparators(movieInfo.ReleaseTokens ?? string.Empty).Trim();
                results = SearchForNewScene($"{studioTitle} {releaseDate} {sceneTitle}".Trim(), null, null, sceneSource);
            }
            else
            {
                // Without a studio id the date isn't sent either, and the search is by name.
                results = SearchForNewScene($"{studioTitle} {releaseDate}", studio?.ForeignId, releaseDate, sceneSource);
            }

            var matches = results.Where(m => m.MovieMetadata.Value.ReleaseDate == releaseDate && isSameStudio(m.MovieMetadata.Value)).ToList();

            if (matches.Count != results.Count)
            {
                _logger.Debug("Dropped {0} of {1} scene search results not from studio '{2}' on {3}", results.Count - matches.Count, results.Count, movieInfo.StudioTitle, releaseDate);
            }

            return matches;
        }

        // An owned scene comes back as the library movie, matched on the locked-source key.
        private List<Movie> LookupScene(string sceneId, SceneMetadataType sceneSource)
        {
            if (sceneId == null)
            {
                return new List<Movie>();
            }

            // StashDB ids are UUIDs and the server answers any other id with a 500, so a malformed one is no result.
            // TPDB also takes numeric ids, and TryGetLookupId has already screened its terms.
            if (sceneSource == SceneMetadataType.StashDb && !Guid.TryParseExact(sceneId, "D", out _))
            {
                _logger.Debug("'{0}' is not a StashDB scene id", sceneId.ForLog());
                return new List<Movie>();
            }

            try
            {
                var movieLookup = GetSceneInfo(sceneId, sceneSource).Item1;
                return movieLookup == null ? new List<Movie>() : new List<Movie> { _movieService.FindByForeignId(movieLookup.ForeignId) ?? new Movie { MovieMetadata = movieLookup } };
            }
            catch (MovieNotFoundException ex)
            {
                _logger.Debug(ex, $"Movie not found");
                return new List<Movie>();
            }
        }

        private List<Movie> SearchForNewScene(string title, string studioForeignId, string releaseDate, SceneMetadataType sceneSource)
        {
            var normalizedTitle = title.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal);
            var isTmdbTerm = normalizedTitle.StartsWith(TmdbPrefix, StringComparison.Ordinal) || normalizedTitle.StartsWith(TmdbIdPrefix, StringComparison.Ordinal);
            var isTpdbTerm = normalizedTitle.StartsWith(TpdbPrefix, StringComparison.Ordinal) || normalizedTitle.StartsWith(TpdbIdPrefix, StringComparison.Ordinal);

            if (sceneSource == SceneMetadataType.Tpdb && isTmdbTerm)
            {
                throw new InvalidSearchTermException("Scene search only supports TPDB ids. Use tpdb:<id> or search by title instead");
            }

            if (sceneSource != SceneMetadataType.Tpdb && (isTmdbTerm || isTpdbTerm))
            {
                throw new InvalidSearchTermException("Scene search only supports StashDB ids. Use stash:<id> or search by title instead");
            }

            try
            {
                var lowerTitle = title.ToLowerInvariant();

                lowerTitle = lowerTitle.Replace(".", "", StringComparison.Ordinal);

                // Allow search to accept a full StashDB URL
                var regex = new Regex(@"^https://stashdb\.org/scenes/(?<stashid>[A-Za-z0-9\-]+).*$", RegexOptions.Compiled, RegexDefaults.Timeout);
                var match = regex.Match(title);

                if (match.Success)
                {
                    lowerTitle = StashPrefixConst + match.Groups["stashid"].Value;
                    _logger.Debug($"Search based on StashDB URL.  Re-writing as {lowerTitle}");
                }

                if (TryGetLookupId(lowerTitle, sceneSource, out var sceneId))
                {
                    return LookupScene(sceneId, sceneSource);
                }

                var searchTerm = ReplaceSearchSeparators(lowerTitle);

                var requestBuilder = _whisparrMetadata.Create()
                    .SetSegment(RouteConst, SceneSourceIds.Route(sceneSource, "scene/search"))
                    .AddQueryParam("q", searchTerm);

                // The metadata server answers a studio and date pair with that studio's scenes on that date.
                // Older servers ignore the pair and search by q, so q is always sent. The TPDB route takes q only.
                if (sceneSource != SceneMetadataType.Tpdb && studioForeignId.IsNotNullOrWhiteSpace() && releaseDate.IsNotNullOrWhiteSpace())
                {
                    requestBuilder.AddQueryParam("studio", studioForeignId)
                        .AddQueryParam("date", releaseDate);
                }

                var request = requestBuilder.Build();

                request.AllowAutoRedirect = true;

                var httpResponse = _httpClient.Get<List<MovieResource>>(request);

                return httpResponse.Resource
                    .Select(resource =>
                    {
                        try
                        {
                            return MapSearchResult(resource, sceneSource);
                        }
                        catch (Exception ex)
                        {
                            _logger.Trace(ex, $"Failed to map Scene search result for resource: {resource?.ToJson(Formatting.None)}");
                            return null;
                        }
                    })
                    .Where(x => x != null)
                    .ToList();
            }
            catch (UnexpectedHtmlContentException ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed. StashDb returned a HTML Response.", title.ForLog());
                return new List<Movie>();
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookExceptionMessageConst, ex, title);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException(SkyHookExceptionMessageConst, ex, title);
            }
            catch (JsonException ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed. StashDb returned a JSON response.", title.ForLog());
                return new List<Movie>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Invalid response received from StashDb.", ex, title);
            }
        }

        private static Studio MapStudio(StudioResource studioResource, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            if (studioResource == null)
            {
                throw new ArgumentNullException(nameof(studioResource));
            }

            // Do not Map the Studio if it has no id in the locked scene source. For Movies as the name is stored in the movie Metadata.
            var studioKey = SceneSourceIds.Key(sceneSource, studioResource.ForeignIds);

            if (studioKey == null)
            {
                return null;
            }

            var newStudio = new Studio
            {
                Title = studioResource.Title,
                CleanTitle = studioResource.Title.CleanStudioTitle(),
                SortTitle = Parser.Parser.NormalizeTitle(studioResource.Title),
                Aliases = studioResource.Aliases,
                Website = studioResource.Homepage,
                ForeignId = studioKey,
                TpdbId = studioResource.ForeignIds.TpdbId,
                TmdbId = studioResource.ForeignIds.TmdbId,
                Network = studioResource.Network,
                Images = studioResource.Images?.Select(MapImage).ToList() ?? new List<MediaCover.MediaCover>(),
                Status = studioResource.Status
            };

            return newStudio;
        }

        // Returns null for a result with no id in its source; the search drops it.
        private Movie MapSearchResult(MovieResource result, SceneMetadataType sceneSource)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var foreignId = MapForeignId(MapMetadataSource(result.ItemType, result.ForeignIds, sceneSource), result.ForeignIds);

            if (foreignId == null)
            {
                return null;
            }

            var movie = _movieService.FindByForeignId(foreignId);

            if (movie == null)
            {
                var metadata = MapMovie(result, sceneSource);
                movie = new Movie { MovieMetadata = metadata };

                if (result.Credits != null)
                {
                    // Names and ids come from the same keyed credits, so a dropped credit leaves no orphan name.
                    var credits = result.Credits.Where(c => c != null).Select(c => MapCast(c, sceneSource)).Where(c => c != null).ToList();

                    metadata.Credits.AddRange(credits);
                    metadata.PerformerNames = credits.Select(c => c.PersonName).ToList();
                    metadata.PerformerForeignIds = credits.Select(c => c.PerformerForeignId).ToList();
                }
            }

            return movie;
        }

        // Returns null when the performer has no id in the locked scene source; it must not be stored keyless.
        private static Performer MapPerformer(PerformerResource performer, SceneMetadataType sceneSource = SceneMetadataType.StashDb)
        {
            if (performer == null)
            {
                throw new ArgumentNullException(nameof(performer));
            }

            var performerKey = SceneSourceIds.Key(sceneSource, performer.ForeignIds);

            if (performerKey == null)
            {
                return null;
            }

            var newPerformer = new Performer
            {
                Name = performer.Name,
                Disambiguation = performer.Disambiguation ?? string.Empty,
                Aliases = performer.Aliases ?? new List<string>(),
                CleanName = performer.Name.CleanMovieTitle(),
                SortName = Parser.Parser.NormalizeTitle(performer.Name),
                Gender = MapGender(performer.Gender),
                Country = performer.Country ?? string.Empty,
                Height = performer.Height,
                CupSize = performer.CupSize,
                BandSize = performer.BandSize,
                HipSize = performer.HipSize,
                WaistSize = performer.WaistSize,
                BreastType = performer.BreastType,
                Status = performer.Status,
                MergedIntoId = performer.MergedIntoId,
                BirthDate = performer.BirthDate,
                DeathDate = performer.DeathDate,
                Age = performer.Age,
                CareerStart = performer.CareerStart,
                CareerEnd = performer.CareerEnd,
                Tattoos = performer.Tattoos ?? new List<string>(),
                Piercings = performer.Piercings ?? new List<string>(),
                Ethnicity = MapEthnicity(performer.Ethnicity),
                EyeColor = MapEyeColor(performer.EyeColor),
                HairColor = MapHairColor(performer.HairColor),
                ForeignId = performerKey,
                TmdbId = performer.ForeignIds.TmdbId,
                TpdbId = performer.ForeignIds.TpdbId,
                Images = performer.Images.Select(MapImage).ToList()
            };

            return newPerformer;
        }
    }
}
