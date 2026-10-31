using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.ProgressMessaging;

namespace NzbDrone.Core.LegacyMigration
{
    // Runs phase 2 of a legacy migration: dispatches to the source's runner and keeps passing until nothing is pending
    public class LegacyMigrationImportService : IExecute<LegacyMigrationImportCommand>, IHandle<ApplicationStartedEvent>
    {
        private const string Area = "Migration";

        private readonly ILegacyMigrationService _migrationService;
        private readonly IEnumerable<ILegacyImportRunner> _runners;
        private readonly IReportCommandProgress _progressReporter;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ISkyhookPacer _pacer;
        private readonly Logger _logger;

        public LegacyMigrationImportService(ILegacyMigrationService migrationService,
                                            IEnumerable<ILegacyImportRunner> runners,
                                            IReportCommandProgress progressReporter,
                                            IManageCommandQueue commandQueueManager,
                                            ISkyhookPacer pacer,
                                            Logger logger)
        {
            _migrationService = migrationService;
            _runners = runners;
            _progressReporter = progressReporter;
            _commandQueueManager = commandQueueManager;
            _pacer = pacer;
            _logger = logger;
        }

        public void Execute(LegacyMigrationImportCommand message)
        {
            var state = _migrationService.GetCurrent();

            if (state == null || state.Completed)
            {
                return;
            }

            var runners = _runners.Where(r => r.Handles(state)).ToList();

            if (runners.Count == 0)
            {
                _logger.Error("No legacy import runner handles {0} migrations", state.SourceKind);

                return;
            }

            if (runners.Count > 1)
            {
                _migrationService.Report(LegacyReportCategory.NeedsAttention,
                    Area,
                    state.SourceKind.ToString(),
                    $"{runners.Count} import runners claim this migration, so none was run");

                return;
            }

            var runner = runners[0];
            var progress = new ImportProgress(_progressReporter);

            // Consecutive passes in which every item failed: the source is down, so the whole import backs off together
            var outagePasses = 0;

            while (true)
            {
                var pass = runner.RunPass(state, progress, outagePasses >= LegacyRetryPolicy.MaxAttempts);

                if (pass.Pending == 0)
                {
                    _migrationService.MarkCompleted();

                    return;
                }

                // A pass that worked on nothing (everything still backing off) says nothing about the outage
                if (pass.Outage)
                {
                    outagePasses++;
                }
                else if (pass.Attempted > 0)
                {
                    outagePasses = 0;
                }

                var delay = DelayAfter(pass, outagePasses);

                if (delay == null)
                {
                    _logger.Error("Legacy import runner left {0} items pending with none waiting to retry", pass.Pending);

                    return;
                }

                _pacer.Sleep(delay.Value);
            }
        }

        // Sleeps until the earliest item is due, and no longer than the 202 delay while anything waits on one.
        // Null when nothing is waiting for anything.
        private TimeSpan? DelayAfter(LegacyImportPass pass, int outagePasses)
        {
            // Nothing was recorded against the items, so the schedule is the service's own until the cap is reached
            if (pass.Outage && outagePasses <= LegacyRetryPolicy.MaxAttempts)
            {
                var outageDelay = LegacyRetryPolicy.DelayBefore(outagePasses);

                _logger.Warn("Every legacy import item tried failed transiently, so the source looks to be down; trying again in {0}", outageDelay);

                return outageDelay;
            }

            TimeSpan? delay = null;

            if (pass.WaitingRetry > 0 && pass.NextDue.HasValue)
            {
                var untilDue = pass.NextDue.Value - _pacer.UtcNow;

                delay = untilDue > TimeSpan.Zero ? untilDue : TimeSpan.Zero;

                _logger.Info("{0} legacy import items failed transiently, retrying in {1}", pass.WaitingRetry, delay);
            }

            if (pass.Waiting > 0)
            {
                _logger.Info("Skyhook is still resolving ids for {0} studio(s), checking again in {1}", pass.Waiting, LegacyRetryPolicy.PendingDelay);

                delay = delay.HasValue && delay.Value < LegacyRetryPolicy.PendingDelay ? delay : LegacyRetryPolicy.PendingDelay;
            }

            return delay;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            if (_migrationService.IsIncomplete())
            {
                _commandQueueManager.Push(new LegacyMigrationImportCommand());
            }
        }

        private sealed class ImportProgress : ILegacyImportProgress
        {
            private readonly IReportCommandProgress _reporter;

            // What's still to do per phase, for the phases a runner gave a total
            private readonly Dictionary<string, long> _phaseRemaining = new();
            private long _total;
            private long _done;

            public ImportProgress(IReportCommandProgress reporter)
            {
                _reporter = reporter;
            }

            public void Total(long total)
            {
                // A runner sets the total at the start of each pass, so the count starts over with it
                _total = total;
                _done = 0;
                _phaseRemaining.Clear();
            }

            public void PhaseTotal(string phase, long total)
            {
                _phaseRemaining[phase] = total;
            }

            public void Advance(string phase, long by = 1)
            {
                _done += by;

                long? remaining = null;

                if (_phaseRemaining.TryGetValue(phase, out var left))
                {
                    remaining = Math.Max(left - by, 0);
                    _phaseRemaining[phase] = remaining.Value;
                }

                var percent = _total > 0 ? (double)_done / _total * 100 : 0;

                _reporter.Report(phase, _done, _total, percent, remaining);
            }
        }
    }
}
