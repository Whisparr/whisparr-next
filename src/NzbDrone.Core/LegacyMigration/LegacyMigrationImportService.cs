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

            while (true)
            {
                var pass = runner.RunPass(state, progress);

                if (pass.Pending == 0)
                {
                    _migrationService.MarkCompleted();

                    return;
                }

                if (pass.WaitingRetry == 0 && pass.Waiting == 0)
                {
                    _logger.Error("Legacy import runner left {0} items pending with none waiting to retry", pass.Pending);

                    return;
                }

                _pacer.Sleep(DelayAfter(pass));
            }
        }

        // The shorter of the two delays when both kinds of item are waiting
        private TimeSpan DelayAfter(LegacyImportPass pass)
        {
            var delay = pass.WaitingRetry > 0 ? LegacyRetryPolicy.DelayBefore(pass.NextAttempt) : LegacyRetryPolicy.PendingDelay;

            if (pass.WaitingRetry > 0 && pass.Waiting > 0 && delay > LegacyRetryPolicy.PendingDelay)
            {
                delay = LegacyRetryPolicy.PendingDelay;
            }

            if (pass.WaitingRetry > 0)
            {
                _logger.Info("{0} legacy import items failed transiently, retrying in {1}", pass.WaitingRetry, delay);
            }

            if (pass.Waiting > 0)
            {
                _logger.Info("Skyhook is still resolving ids for {0} studio(s), checking again in {1}", pass.Waiting, delay);
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
            }

            public void Advance(string phase, long by = 1)
            {
                _done += by;

                var percent = _total > 0 ? (double)_done / _total * 100 : 0;

                _reporter.Report(phase, _done, _total, percent);
            }
        }
    }
}
