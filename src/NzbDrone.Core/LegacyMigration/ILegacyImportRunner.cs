namespace NzbDrone.Core.LegacyMigration
{
    // The outcome of one sweep over the items a runner still has to import
    public class LegacyImportPass
    {
        // Items still to do
        public int Pending { get; set; }

        // Items that failed transiently this pass
        public int WaitingRetry { get; set; }

        // Items still waiting on server-side work this pass; not failures, so they spend no attempts
        public int Waiting { get; set; }

        // The smallest attempt count among the waiting items; picks the delay before the next pass
        public int NextAttempt { get; set; }
    }

    public interface ILegacyImportProgress
    {
        void Total(long total);
        void Advance(string phase, long by = 1);
    }

    // One per migration source kind; the import command dispatches to the one that handles the state
    public interface ILegacyImportRunner
    {
        bool Handles(LegacyMigrationState state);
        LegacyImportPass RunPass(LegacyMigrationState state, ILegacyImportProgress progress);
    }
}
