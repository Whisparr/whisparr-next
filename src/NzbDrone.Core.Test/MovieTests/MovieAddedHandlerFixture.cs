using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieAddedHandlerFixture : CoreTest<MovieAddedHandler>
    {
        private List<Movie> _movies;

        [SetUp]
        public void Setup()
        {
            _movies = Builder<Movie>.CreateListOfSize(3).Build().ToList();
        }

        [Test]
        public void should_not_queue_refresh_for_inline_imports()
        {
            Subject.Handle(new MoviesImportedEvent(_movies, refreshInline: true));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.PushMany(It.IsAny<List<RefreshMovieCommand>>()), Times.Never());
        }

        [Test]
        public void should_queue_refresh_for_regular_imports()
        {
            Subject.Handle(new MoviesImportedEvent(_movies, refreshInline: false));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.PushMany(It.Is<List<RefreshMovieCommand>>(c => c.Count == 3 && c.All(x => x.IsNewMovie))), Times.Once());
        }
    }
}
