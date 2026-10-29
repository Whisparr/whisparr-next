using System;

namespace NzbDrone.Core.LegacyMigration
{
    // The outcome of one sweep over the items a runner still has to import
    public class LegacyImportPass
    {
        // Items still to do
        public int Pending { get; set; }

        // Items backing off after a transient failure, whether it happened this pass or an earlier one
        public int WaitingRetry { get; set; }

        // Items still waiting on server-side work this pass; not failures, so they spend no attempts
        public int Waiting { get; set; }

        // Items worked on this pass; items still backing off from an earlier failure are skipped and not counted
        public int Attempted { get; set; }

        // The earliest moment an item that is backing off becomes due again
        public DateTime? NextDue { get; set; }

        // Every item worked on failed transiently and none was waiting on server-side work: the source looks to be
        // down, not the items. Unless the pass was told to record outages, no attempt was spent on them.
        public bool Outage { get; set; }
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

        // recordOutages: spend attempts even when the whole pass failed, once the service has waited out enough outage passes
        LegacyImportPass RunPass(LegacyMigrationState state, ILegacyImportProgress progress, bool recordOutages);
    }
}
