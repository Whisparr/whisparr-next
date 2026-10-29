using System.Collections.Generic;
using NLog;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Movies
{
    public class MovieScannedHandler : IHandle<MovieScannedEvent>,
                                        IHandle<MovieScanSkippedEvent>
    {
        private readonly IMovieService _movieService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ILegacyMigrationGate _migrationGate;

        private readonly Logger _logger;

        public MovieScannedHandler(IMovieService movieService,
                                    IManageCommandQueue commandQueueManager,
                                    ILegacyMigrationGate migrationGate,
                                    Logger logger)
        {
            _migrationGate = migrationGate;
            _movieService = movieService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        private void HandleScanEvents(Movie movie)
        {
            var addOptions = movie.AddOptions;

            if (addOptions == null)
            {
                return;
            }

            _logger.Info("[{0}] was recently added, performing post-add actions", movie.Title);

            // Adds made during the library migration never search; AddOptions is still cleared below.
            if (addOptions.SearchForMovie && !_migrationGate.AutomationPaused)
            {
                _commandQueueManager.Push(new MoviesSearchCommand { MovieIds = new List<int> { movie.Id } });
            }

            movie.AddOptions = null;
            _movieService.RemoveAddOptions(movie);
        }

        public void Handle(MovieScannedEvent message)
        {
            HandleScanEvents(message.Movie);
        }

        public void Handle(MovieScanSkippedEvent message)
        {
            HandleScanEvents(message.Movie);
        }
    }
}
