using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Movies
{
    public interface IRescanAfterRefreshFilter
    {
        /// <summary>
        /// Applies the RescanAfterRefresh setting. When false, publishes the matching MovieScanSkippedEvent.
        /// </summary>
        bool ShouldRescan(Movie movie, bool isNew, CommandTrigger trigger);
    }

    public class RescanAfterRefreshFilter : IRescanAfterRefreshFilter
    {
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public RescanAfterRefreshFilter(IConfigService configService, IEventAggregator eventAggregator, Logger logger)
        {
            _configService = configService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public bool ShouldRescan(Movie movie, bool isNew, CommandTrigger trigger)
        {
            var rescanAfterRefresh = _configService.RescanAfterRefresh;

            if (isNew)
            {
                _logger.Trace("Forcing rescan of {0}. Reason: New movie", movie);
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.Never)
            {
                _logger.Trace("Skipping rescan of {0}. Reason: Never rescan after refresh", movie);
                _eventAggregator.PublishEvent(new MovieScanSkippedEvent(movie, MovieScanSkippedReason.NeverRescanAfterRefresh));

                return false;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.AfterManual && trigger != CommandTrigger.Manual)
            {
                _logger.Trace("Skipping rescan of {0}. Reason: Not after automatic scans", movie);
                _eventAggregator.PublishEvent(new MovieScanSkippedEvent(movie, MovieScanSkippedReason.RescanAfterManualRefreshOnly));

                return false;
            }

            return true;
        }
    }
}
