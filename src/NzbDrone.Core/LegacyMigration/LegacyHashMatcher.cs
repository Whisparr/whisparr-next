using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;

namespace NzbDrone.Core.LegacyMigration
{
    public sealed record HashQuery(string Key, string Path, string KnownOsHash);

    public sealed record HashMatch(string SceneId, string OsHash, bool TryLater);

    public interface ILegacyHashMatcher
    {
        // One entry per input key.
        Dictionary<string, HashMatch> Match(IReadOnlyCollection<HashQuery> files);
    }

    // Identifies files by oshash only; a file that hashes to more than one scene is left unmatched.
    public class LegacyHashMatcher : ILegacyHashMatcher
    {
        private const string OsHashAlgorithm = "OSHASH";

        private readonly IVideoHashService _videoHashService;
        private readonly IFingerprintLookupClient _lookupClient;
        private readonly Logger _logger;

        public LegacyHashMatcher(IVideoHashService videoHashService,
            IFingerprintLookupClient lookupClient,
            Logger logger)
        {
            _videoHashService = videoHashService;
            _lookupClient = lookupClient;
            _logger = logger;
        }

        public Dictionary<string, HashMatch> Match(IReadOnlyCollection<HashQuery> files)
        {
            var results = new Dictionary<string, HashMatch>();
            var fingerprints = new List<FileFingerprint>();

            foreach (var file in files)
            {
                var osHash = file.KnownOsHash ?? Hash(file.Path);

                if (osHash == null)
                {
                    results[file.Key] = new HashMatch(null, null, false);
                    continue;
                }

                results[file.Key] = new HashMatch(null, osHash, false);
                fingerprints.Add(new FileFingerprint { Key = file.Key, OsHash = osHash });
            }

            if (fingerprints.Count == 0)
            {
                return results;
            }

            var lookup = _lookupClient.Lookup(fingerprints, false);

            foreach (var fingerprint in fingerprints)
            {
                if (!lookup.TryGetValue(fingerprint.Key, out var result))
                {
                    continue;
                }

                if (result.Status == FingerprintLookupStatus.TryLater)
                {
                    results[fingerprint.Key] = new HashMatch(null, fingerprint.OsHash, true);
                    continue;
                }

                var sceneIds = (result.Candidates ?? new List<FingerprintCandidateResult>())
                    .Where(c => string.Equals(c.Algorithm, OsHashAlgorithm, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.SceneId)
                    .Distinct()
                    .Take(2)
                    .ToList();

                if (sceneIds.Count == 1)
                {
                    results[fingerprint.Key] = new HashMatch(sceneIds[0], fingerprint.OsHash, false);
                }
            }

            return results;
        }

        private string Hash(string path)
        {
            try
            {
                return _videoHashService.ComputeOsHash(path);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not hash {0}", path);
                return null;
            }
        }
    }
}
