using NzbDrone.Common.Http;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.LegacyMigration.Events;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(LegacyMigrationCompletedEvent))]
    [CheckOn(typeof(LegacyMigrationReportDismissedEvent))]
    public class LegacyMigrationCheck : HealthCheckBase
    {
        private readonly ILegacyMigrationService _legacyMigrationService;

        public LegacyMigrationCheck(ILegacyMigrationService legacyMigrationService,
            ILocalizationService localizationService)
            : base(localizationService)
        {
            _legacyMigrationService = legacyMigrationService;
        }

        public override HealthCheck Check()
        {
            var state = _legacyMigrationService.GetCurrent();

            if (state == null || (state.Completed && state.ReportDismissed))
            {
                return new HealthCheck(GetType());
            }

            // Scheme-less URLs are in-app routes in the frontend Link
            if (!state.Completed)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Notice,
                    HealthCheckReason.LegacyMigrationInProgress,
                    _localizationService.GetLocalizedString("LegacyMigrationInProgressHealthCheckMessage"))
                {
                    WikiUrl = new HttpUri("/system/tasks")
                };
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Notice,
                HealthCheckReason.LegacyMigrationReport,
                _localizationService.GetLocalizedString("LegacyMigrationReportHealthCheckMessage"))
            {
                WikiUrl = new HttpUri("/system/migration-report")
            };
        }
    }
}
