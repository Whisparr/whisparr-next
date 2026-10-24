using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieServiceTests
{
    [TestFixture]
    public class DeleteMovieFixture : CoreTest<MovieService>
    {
        private Movie _movie;
        private List<string> _events;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew().With(m => m.Id = 1).Build();
            _events = new List<string>();

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Get(1))
                  .Returns(_movie);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Movie> { _movie });

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Delete(1))
                  .Callback(() => _events.Add("rows deleted"));

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.DeleteMany(It.IsAny<IEnumerable<int>>()))
                  .Callback(() => _events.Add("rows deleted"));

            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<MoviesDeletingEvent>()))
                  .Callback<MoviesDeletingEvent>(e =>
                  {
                      _events.Add("deleting");
                      e.OwnedFilePaths[1] = new List<string> { "/lib/scenes/Shady Spa/scene one.mp4" };
                  });
        }

        [Test]
        public void should_capture_owned_paths_before_rows_are_deleted_and_pass_them_on()
        {
            Subject.DeleteMovie(1, true);

            _events.Should().Equal("deleting", "rows deleted");

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MoviesDeletedEvent>(e => e.OwnedFilePaths[1].Count == 1)), Times.Once());
        }

        [Test]
        public void should_capture_owned_paths_before_rows_are_deleted_for_bulk_delete()
        {
            Subject.DeleteMovies(new List<int> { 1 }, true);

            _events.Should().Equal("deleting", "rows deleted");

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MoviesDeletedEvent>(e => e.OwnedFilePaths[1].Count == 1)), Times.Once());
        }

        [Test]
        public void should_not_capture_owned_paths_when_files_are_kept()
        {
            Subject.DeleteMovie(1, false);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<MoviesDeletingEvent>()), Times.Never());

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MoviesDeletedEvent>(e => e.OwnedFilePaths.Count == 0)), Times.Once());
        }
    }
}
