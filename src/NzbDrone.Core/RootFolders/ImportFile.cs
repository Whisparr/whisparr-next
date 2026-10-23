using System;
using NzbDrone.Core.Datastore;

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
    }
}
