using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Core.LegacyMigration.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.LegacyMigration
{
    public interface ILegacyMigrationService
    {
        LegacyMigrationState GetCurrent();
        bool IsIncomplete();
        void SaveProgress(LegacyMigrationState state);
        void MarkCompleted();
        void DismissReport();
        void Report(LegacyReportCategory category, string area, string subject, string detail, int count = 1);
        List<LegacyMigrationReportItem> GetReport();
    }

    public class LegacyMigrationService : ILegacyMigrationService
    {
        private const string PhaseDone = "Done";
        private const string IncompleteKey = "incomplete";

        private readonly ILegacyMigrationStateRepository _stateRepository;
        private readonly ILegacyMigrationReportRepository _reportRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICached<bool> _incompleteCache;

        public LegacyMigrationService(ILegacyMigrationStateRepository stateRepository,
                                      ILegacyMigrationReportRepository reportRepository,
                                      IEventAggregator eventAggregator,
                                      ICacheManager cacheManager)
        {
            _stateRepository = stateRepository;
            _reportRepository = reportRepository;
            _eventAggregator = eventAggregator;
            _incompleteCache = cacheManager.GetCache<bool>(GetType(), "incomplete");
        }

        public LegacyMigrationState GetCurrent()
        {
            return _stateRepository.GetCurrent();
        }

        public bool IsIncomplete()
        {
            return _incompleteCache.Get(IncompleteKey, () =>
            {
                var state = _stateRepository.GetCurrent();

                return state != null && !state.Completed;
            });
        }

        public void SaveProgress(LegacyMigrationState state)
        {
            if (state.Id == 0)
            {
                _stateRepository.Insert(state);
            }
            else
            {
                _stateRepository.Update(state);
            }

            // The first save is what makes a migration incomplete
            _incompleteCache.Clear();
        }

        public void MarkCompleted()
        {
            var state = _stateRepository.GetCurrent();

            if (state == null)
            {
                return;
            }

            state.Completed = true;
            state.CompletedAt = DateTime.UtcNow;
            state.Phase = PhaseDone;
            _stateRepository.Update(state);

            _incompleteCache.Clear();
            _eventAggregator.PublishEvent(new LegacyMigrationCompletedEvent());
        }

        public void DismissReport()
        {
            var state = _stateRepository.GetCurrent();

            if (state == null)
            {
                return;
            }

            state.ReportDismissed = true;
            _stateRepository.Update(state);

            _eventAggregator.PublishEvent(new LegacyMigrationReportDismissedEvent());
        }

        public void Report(LegacyReportCategory category, string area, string subject, string detail, int count = 1)
        {
            _reportRepository.Insert(new LegacyMigrationReportItem
            {
                Category = category,
                Area = area,
                Subject = subject,
                Detail = detail,
                Count = count
            });
        }

        public List<LegacyMigrationReportItem> GetReport()
        {
            return _reportRepository.All().ToList();
        }
    }
}
