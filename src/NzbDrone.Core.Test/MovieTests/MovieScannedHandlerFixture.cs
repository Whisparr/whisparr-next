using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieScannedHandlerFixture : CoreTest<MovieScannedHandler>
    {
        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.AddOptions = new AddMovieOptions { SearchForMovie = true })
                                   .Build();
        }

        private void GivenPaused(bool paused)
        {
            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(g => g.AutomationPaused)
                  .Returns(paused);
        }

        [Test]
        public void search_on_add_not_pushed_while_paused_and_add_options_cleared()
        {
            GivenPaused(true);

            Subject.Handle(new MovieScannedEvent(_movie, null));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<MoviesSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());

            Mocker.GetMock<IMovieService>()
                  .Verify(s => s.RemoveAddOptions(_movie), Times.Once());
        }

        [Test]
        public void search_on_add_pushed_when_not_paused()
        {
            GivenPaused(false);

            Subject.Handle(new MovieScannedEvent(_movie, null));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<MoviesSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());

            Mocker.GetMock<IMovieService>()
                  .Verify(s => s.RemoveAddOptions(_movie), Times.Once());
        }
    }
}
