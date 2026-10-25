using System;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public class MigrationContext
    {
        public static MigrationContext Current { get; set; }

        public MigrationType MigrationType { get; private set; }
        public long? DesiredVersion { get; set; }
        public Action<NzbDroneMigrationBase> BeforeMigration { get; set; }

        // Set only by the migration wizard, which migrates a copy of a legacy database.
        public bool AllowLegacySource { get; set; }

        public MigrationContext(MigrationType migrationType, long? desiredVersion = null)
        {
            MigrationType = migrationType;
            DesiredVersion = desiredVersion;
        }
    }
}
