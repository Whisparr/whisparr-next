using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(300)]
    public class whisparr_v5 : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // No schema change: this migration's VersionInfo row is the marker LegacyDatabaseGuard looks for.
        }

        protected override void LogDbUpgrade()
        {
            // No schema change: this migration's VersionInfo row is the marker LegacyDatabaseGuard looks for.
        }
    }
}
