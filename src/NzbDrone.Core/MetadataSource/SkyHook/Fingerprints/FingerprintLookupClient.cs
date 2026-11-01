using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.SkyHook.Fingerprints
{
    public interface IFingerprintLookupClient
    {
        // One entry per input key, never throws; a failed batch yields TryLater for its keys.
        Dictionary<string, FingerprintLookupResult> Lookup(IReadOnlyCollection<FileFingerprint> files, bool localOnly);

        // batchDone gets how many files each finished batch held, for callers showing progress on a long lookup.
        Dictionary<string, FingerprintLookupResult> Lookup(IReadOnlyCollection<FileFingerprint> files, bool localOnly, Action<int> batchDone);
    }

    public class FingerprintLookupClient : IFingerprintLookupClient
    {
        private const string RouteConst = "route";
        private const string LookupRoute = "fingerprint/lookup";
        private const int LocalBatchSize = 100;
        private const int StashDbUpstreamBatchSize = 100;
        private const int TpdbUpstreamBatchSize = 20;

        private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(180);

        // A local-only lookup is a database read on the service; if it takes longer something is wrong.
        private static readonly TimeSpan LocalLookupTimeout = TimeSpan.FromSeconds(15);

        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _whisparrMetadata;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public FingerprintLookupClient(IHttpClient httpClient,
            IConfigFileProvider configFileProvider,
            IConfigService configService,
            Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;

            var whisparrMetadata = configFileProvider.WhisparrMetadata;
            if (whisparrMetadata.IsNullOrWhiteSpace())
            {
                whisparrMetadata = "https://api.whisparr.com/v5/{route}";
            }

            _whisparrMetadata = new HttpRequestBuilder(whisparrMetadata).CreateFactory();
        }

        public Dictionary<string, FingerprintLookupResult> Lookup(IReadOnlyCollection<FileFingerprint> files, bool localOnly)
        {
            return Lookup(files, localOnly, null);
        }

        public Dictionary<string, FingerprintLookupResult> Lookup(IReadOnlyCollection<FileFingerprint> files, bool localOnly, Action<int> batchDone)
        {
            var results = new Dictionary<string, FingerprintLookupResult>();

            if (files == null || files.Count == 0)
            {
                return results;
            }

            var sendable = files.Where(f => f?.Key != null)
                                .DistinctBy(f => f.Key)
                                .ToList();

            foreach (var file in sendable.Where(f => !HasHash(f)))
            {
                results[file.Key] = Result(file.Key, FingerprintLookupStatus.NotFound);
            }

            sendable = sendable.Where(HasHash).ToList();

            var source = _configService.WhisparrSceneMetadataSource;
            var sourceName = source == SceneMetadataType.Tpdb ? "tpdb" : "stashDb";

            var unreachable = false;

            foreach (var batch in sendable.Chunk(BatchSize(source, localOnly)))
            {
                // A timeout or transport failure means the service is slow or down: don't make every later batch wait too
                var batchResults = unreachable
                    ? new Dictionary<string, FingerprintLookupResult>()
                    : LookupBatch(batch, sourceName, localOnly, out unreachable);

                foreach (var key in batch.Select(f => f.Key))
                {
                    results[key] = batchResults.TryGetValue(key, out var result)
                        ? result
                        : Result(key, FingerprintLookupStatus.TryLater);
                }

                batchDone?.Invoke(batch.Length);
            }

            return results;
        }

        private static bool IsTransportFailure(Exception ex)
        {
            return ex is WebException or HttpRequestException or TimeoutException or OperationCanceledException or IOException;
        }

        private Dictionary<string, FingerprintLookupResult> LookupBatch(FileFingerprint[] batch, string sourceName, bool localOnly, out bool unreachable)
        {
            var mapped = new Dictionary<string, FingerprintLookupResult>();

            unreachable = false;

            try
            {
                var body = new FingerprintLookupRequestResource
                {
                    Source = sourceName,
                    LocalOnly = localOnly,
                    Files = batch.Select(f => new FingerprintLookupRequestFileResource
                    {
                        Key = f.Key,
                        Oshash = f.OsHash.IsNullOrWhiteSpace() ? null : f.OsHash,
                        Phash = f.PHash.IsNullOrWhiteSpace() ? null : f.PHash,
                        Duration = f.Duration
                    }).ToList()
                };

                var request = _whisparrMetadata.Create()
                                               .SetSegment(RouteConst, LookupRoute)
                                               .Build();

                request.AllowAutoRedirect = true;
                request.SuppressHttpError = true;
                request.RequestTimeout = localOnly ? LocalLookupTimeout : LookupTimeout;
                request.Headers.ContentType = "application/json";
                request.SetContent(body.ToJson());
                request.ContentSummary = $"{batch.Length} fingerprint(s), source {sourceName}, localOnly {localOnly}";

                var response = _httpClient.Post(request);

                if ((int)response.StatusCode is < 200 or > 299)
                {
                    throw new HttpException(request, response);
                }

                var resource = Json.Deserialize<FingerprintLookupResponseResource>(response.Content);

                foreach (var file in resource?.Files ?? new List<FingerprintLookupResponseFileResource>())
                {
                    if (file?.Key == null)
                    {
                        continue;
                    }

                    mapped[file.Key] = new FingerprintLookupResult
                    {
                        Key = file.Key,
                        Status = MapStatus(file.Status),
                        Candidates = file.Candidates ?? new List<FingerprintCandidateResult>()
                    };
                }
            }
            catch (Exception ex)
            {
                // Message only by design: a stack trace per failed batch is noise, the next pass retries.
#pragma warning disable S6667
                _logger.Warn("Fingerprint lookup failed for {0} file(s): {1}", batch.Length, ex.Message);
#pragma warning restore S6667
                mapped.Clear();
                unreachable = IsTransportFailure(ex);
            }

            return mapped;
        }

        private static bool HasHash(FileFingerprint file)
        {
            return file.OsHash.IsNotNullOrWhiteSpace() || file.PHash.IsNotNullOrWhiteSpace();
        }

        private static int BatchSize(SceneMetadataType source, bool localOnly)
        {
            if (localOnly)
            {
                return LocalBatchSize;
            }

            return source == SceneMetadataType.Tpdb ? TpdbUpstreamBatchSize : StashDbUpstreamBatchSize;
        }

        private static FingerprintLookupStatus MapStatus(string status)
        {
            if (string.Equals(status, "matched", StringComparison.OrdinalIgnoreCase))
            {
                return FingerprintLookupStatus.Matched;
            }

            if (string.Equals(status, "notFound", StringComparison.OrdinalIgnoreCase))
            {
                return FingerprintLookupStatus.NotFound;
            }

            return FingerprintLookupStatus.TryLater;
        }

        private static FingerprintLookupResult Result(string key, FingerprintLookupStatus status)
        {
            return new FingerprintLookupResult { Key = key, Status = status };
        }
    }
}
