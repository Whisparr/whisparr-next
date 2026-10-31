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

        // The passes of runners that work in stages, as one: counts add up, the earliest retry wins, and the source
        // only counts as down when every stage that tried anything found it down
        public static LegacyImportPass Combine(LegacyImportPass a, LegacyImportPass b)
        {
            return new LegacyImportPass
            {
                Pending = a.Pending + b.Pending,
                WaitingRetry = a.WaitingRetry + b.WaitingRetry,
                Waiting = a.Waiting + b.Waiting,
                Attempted = a.Attempted + b.Attempted,
                NextDue = LegacyRetryPolicy.Earlier(a.NextDue, b.NextDue),
                Outage = (a.Attempted == 0 || a.Outage) && (b.Attempted == 0 || b.Outage) && (a.Attempted + b.Attempted) > 0
            };
        }
    }

    // What a runner needs of a seed to schedule it: whether it is done, and how it has been failing
    public interface ILegacySeed
    {
        bool Resolved { get; set; }
        int Attempts { get; set; }
        DateTime? LastAttemptAt { get; set; }
    }

    public interface ILegacyImportProgress
    {
        void Total(long total);
        void PhaseTotal(string phase, long total);
        void Advance(string phase, long by = 1);

        // Long work that finishes no item yet: shows the step and what it has left, and leaves the overall count alone.
        void Status(string phase, long remaining);
    }

    // One per migration source kind; the import command dispatches to the one that handles the state
    public interface ILegacyImportRunner
    {
        bool Handles(LegacyMigrationState state);

        // recordOutages: spend attempts even when the whole pass failed, once the service has waited out enough outage passes
        LegacyImportPass RunPass(LegacyMigrationState state, ILegacyImportProgress progress, bool recordOutages);
    }
}
