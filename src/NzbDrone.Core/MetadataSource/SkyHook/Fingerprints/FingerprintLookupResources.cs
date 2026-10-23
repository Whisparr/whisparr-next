using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.SkyHook.Fingerprints
{
    public class FileFingerprint
    {
        public string Key { get; set; }
        public string OsHash { get; set; }
        public string PHash { get; set; }
        public double? Duration { get; set; }
    }

    public enum FingerprintLookupStatus
    {
        Matched,
        NotFound,
        TryLater
    }

    public class FingerprintCandidateResult
    {
        public string SceneId { get; set; }
        public string Algorithm { get; set; }
        public int Distance { get; set; }
        public int Submissions { get; set; }
        public int? Duration { get; set; }
    }

    public class FingerprintLookupResult
    {
        public string Key { get; set; }
        public FingerprintLookupStatus Status { get; set; }
        public List<FingerprintCandidateResult> Candidates { get; set; } = new();
    }

    // Wire shapes for POST fingerprint/lookup.
    public class FingerprintLookupRequestResource
    {
        public string Source { get; set; }
        public bool LocalOnly { get; set; }
        public List<FingerprintLookupRequestFileResource> Files { get; set; } = new();
    }

    public class FingerprintLookupRequestFileResource
    {
        public string Key { get; set; }
        public string Oshash { get; set; }
        public string Phash { get; set; }
        public double? Duration { get; set; }
    }

    public class FingerprintLookupResponseResource
    {
        public List<FingerprintLookupResponseFileResource> Files { get; set; }
    }

    public class FingerprintLookupResponseFileResource
    {
        public string Key { get; set; }
        public string Status { get; set; }
        public List<FingerprintCandidateResult> Candidates { get; set; }
    }
}
