using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.Fingerprints;

namespace NzbDrone.Core.RootFolders
{
    public class ImportFile : ModelBase
    {
        public int RootFolderId { get; set; }
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public string Name { get; set;  }
        public string ForeignId { get; set; }
        public int QualityProfileId { get; set; }
        public string OsHash { get; set; }
        public string PHash { get; set; }
        public DateTime? PHashAttemptedAt { get; set; }
        public DateTime? HashedFileModifiedAt { get; set; }

        // Null until the first lookup; an empty list means the lookup found nothing
        public List<FingerprintCandidate> Candidates { get; set; }
        public DateTime? CandidatesCheckedAt { get; set; }

        // Hidden from the import and unmapped lists, and kept so a scan doesn't bring the file back
        public bool Dismissed { get; set; }
    }
}
