using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.LegacyMigration
{
    // A v2 Series (one studio/site), staged for the porter. LegacyId is the v2 Id.
    public class LegacyV2Series : ModelBase
    {
        public int LegacyId { get; set; }

        // v2 stores the numeric TPDB site id in TvdbId
        public int TpdbSiteId { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public string RootFolderPath { get; set; }
        public bool Monitored { get; set; }
        public int MonitorNewItems { get; set; }
        public int QualityProfileId { get; set; }

        // JSON, as stored on Series
        public string Tags { get; set; }
        public int SeriesType { get; set; }
        public DateTime? Added { get; set; }
        public bool Done { get; set; }
    }
}
