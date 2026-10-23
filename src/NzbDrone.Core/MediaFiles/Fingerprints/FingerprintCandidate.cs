using System.Collections.Generic;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    // Stored as JSON on ImportFiles and returned on manual import items.
    public class FingerprintCandidate
    {
        public string SceneId { get; set; }

        // Ready-made movie lookup term that resolves straight to this scene under the locked source.
        public string LookupTerm { get; set; }
        public string Algorithm { get; set; }
        public int Distance { get; set; }
        public int Submissions { get; set; }
        public int? Duration { get; set; }
        public int? MovieId { get; set; }
        public string Title { get; set; }
        public string StudioTitle { get; set; }
        public string ReleaseDate { get; set; }
    }

    public class FileMatchOutcome
    {
        public MatchTier Tier { get; set; }
        public Movie Target { get; set; }
        public bool NeedsReview { get; set; }
        public bool Conflict { get; set; }
        public List<FingerprintCandidate> Candidates { get; set; } = new();
    }
}
