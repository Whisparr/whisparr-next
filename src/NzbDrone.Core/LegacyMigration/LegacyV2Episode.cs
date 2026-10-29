using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.LegacyMigration
{
    // A v2 Episode (one scene), staged for the porter. LegacyId is the v2 Id.
    public class LegacyV2Episode : ModelBase
    {
        public int LegacyId { get; set; }
        public int LegacySeriesId { get; set; }

        // v2 stores the numeric TPDB scene id in TvdbId
        public int TpdbSceneId { get; set; }
        public string ExternalId { get; set; }
        public string Title { get; set; }

        // As v2 stores it
        public string AirDate { get; set; }
        public bool Monitored { get; set; }
        public int LegacyEpisodeFileId { get; set; }
        public bool Done { get; set; }
    }
}
