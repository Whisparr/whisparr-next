using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.LegacyMigration
{
    // A performer from the Eros library, kept with its settings until it is matched on the new scene source and added there.
    public class LegacyPerformerSeed : ModelBase
    {
        public string LegacyForeignId { get; set; }
        public string LegacyTpdbId { get; set; }
        public string Name { get; set; }
        public bool Monitored { get; set; }
        public bool MoviesMonitored { get; set; }
        public bool WhisparrMonitorNewItems { get; set; }
        public DateTime? AfterDate { get; set; }
        public string RootFolderPath { get; set; }
        public int QualityProfileId { get; set; }

        // JSON, as stored on the performer
        public string Tags { get; set; }
        public string NewForeignId { get; set; }
        public bool Resolved { get; set; }
        public int Attempts { get; set; }
        public DateTime? LastAttemptAt { get; set; }
    }
}
