using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(309)]
    public class add_legacy_v2_waiting_since : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Skyhook answering 202 spends no attempt, so the first 202 is remembered to put a ceiling on the wait.
            Alter.Table("LegacyV2Series").AddColumn("WaitingSince").AsDateTimeOffset().Nullable();
        }
    }
}
