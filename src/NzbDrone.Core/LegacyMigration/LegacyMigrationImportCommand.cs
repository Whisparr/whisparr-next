using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.LegacyMigration
{
    // Phase 2 of a legacy migration: one long command that keeps going until every item is imported
    public class LegacyMigrationImportCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => false;

        public override bool IsLongRunning => true;
    }
}
