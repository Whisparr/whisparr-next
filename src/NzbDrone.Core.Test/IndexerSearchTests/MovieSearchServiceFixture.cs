using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class MovieSearchServiceFixture : CoreTest<MovieSearchService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.MoviesWithoutFiles(It.IsAny<PagingSpec<Movie>>(), It.IsAny<HashSet<int>>()))
                  .Returns(new PagingSpec<Movie> { Records = new List<Movie>() });

            Mocker.GetMock<IMovieCutoffService>()
                  .Setup(s => s.MoviesWhereCutoffUnmet(It.IsAny<PagingSpec<Movie>>(), It.IsAny<HashSet<int>>(), It.IsAny<List<int>>()))
                  .Returns(new PagingSpec<Movie> { Records = new List<Movie>() });

            Mocker.GetMock<IQueueService>()
                  .Setup(s => s.GetQueue())
                  .Returns(new List<Queue.Queue>());
        }

        private void GivenPaused(bool paused)
        {
            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(g => g.AutomationPaused)
                  .Returns(paused);
        }

        private void VerifyMissingQueried(Times times)
        {
            Mocker.GetMock<IMovieService>()
                  .Verify(s => s.MoviesWithoutFiles(It.IsAny<PagingSpec<Movie>>(), It.IsAny<HashSet<int>>()), times);
        }

        private void VerifyCutoffQueried(Times times)
        {
            Mocker.GetMock<IMovieCutoffService>()
                  .Verify(s => s.MoviesWhereCutoffUnmet(It.IsAny<PagingSpec<Movie>>(), It.IsAny<HashSet<int>>(), It.IsAny<List<int>>()), times);
        }

        [Test]
        public void scheduled_missing_search_skipped_while_paused()
        {
            GivenPaused(true);

            Subject.Execute(new MissingMoviesSearchCommand { Trigger = CommandTrigger.Scheduled });

            VerifyMissingQueried(Times.Never());
        }

        [Test]
        public void manual_missing_search_runs_while_paused()
        {
            GivenPaused(true);

            Subject.Execute(new MissingMoviesSearchCommand { Trigger = CommandTrigger.Manual });

            VerifyMissingQueried(Times.Once());
        }

        [Test]
        public void scheduled_cutoff_search_skipped_while_paused()
        {
            GivenPaused(true);

            Subject.Execute(new CutoffUnmetMoviesSearchCommand { Trigger = CommandTrigger.Scheduled });

            VerifyCutoffQueried(Times.Never());
        }

        [Test]
        public void manual_cutoff_search_runs_while_paused()
        {
            GivenPaused(true);

            Subject.Execute(new CutoffUnmetMoviesSearchCommand { Trigger = CommandTrigger.Manual });

            VerifyCutoffQueried(Times.Once());
        }

        [Test]
        public void scheduled_searches_run_when_not_paused()
        {
            GivenPaused(false);

            Subject.Execute(new MissingMoviesSearchCommand { Trigger = CommandTrigger.Scheduled });
            Subject.Execute(new CutoffUnmetMoviesSearchCommand { Trigger = CommandTrigger.Scheduled });

            VerifyMissingQueried(Times.Once());
            VerifyCutoffQueried(Times.Once());
        }
    }
}
