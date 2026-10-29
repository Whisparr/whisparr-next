using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.LegacyMigration
{
    // A v2 EpisodeFile, staged for the porter. LegacyId is the v2 Id. Path is the series path joined to RelativePath.
    public class LegacyV2EpisodeFile : ModelBase
    {
        public int LegacyId { get; set; }
        public int LegacySeriesId { get; set; }
        public string RelativePath { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }

        // JSON, as stored on EpisodeFiles
        public string Quality { get; set; }

        // JSON array of v2 language ids
        public string Languages { get; set; }
        public string ReleaseGroup { get; set; }
        public string SceneName { get; set; }
        public string OriginalFilePath { get; set; }
        public string MediaInfo { get; set; }
        public int IndexerFlags { get; set; }
        public DateTime DateAdded { get; set; }
        public bool Done { get; set; }
    }
}
