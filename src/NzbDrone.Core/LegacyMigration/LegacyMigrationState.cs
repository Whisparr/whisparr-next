using System;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;

namespace NzbDrone.Core.LegacyMigration
{
    // The single row recording how far a legacy-to-v5 migration got.
    public class LegacyMigrationState : ModelBase
    {
        public LegacyKind SourceKind { get; set; }
        public SceneMetadataType SceneSource { get; set; }
        public MovieMetadataType MovieSource { get; set; }
        public string Phase { get; set; }

        // JSON, owned by whichever phase is running
        public string Progress { get; set; }
        public bool Completed { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string BackupPath { get; set; }
        public bool ReportDismissed { get; set; }

        // Set when an Eros library is being rebuilt on a new scene source rather than copied
        public bool Rebuild { get; set; }
    }
}
