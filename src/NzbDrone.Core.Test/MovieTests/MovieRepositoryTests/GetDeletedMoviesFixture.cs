using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieRepositoryTests
{
    [TestFixture]
    public class GetDeletedMoviesFixture : DbTest<MovieRepository, Movie>
    {
        [Test]
        public void should_only_return_movies_removed_from_the_metadata_source()
        {
            GivenItem("metadata-1", MovieStatusType.Released);
            var deleted = GivenItem("metadata-2", MovieStatusType.Deleted);

            var movies = Subject.GetDeletedMovies();

            movies.Should().ContainSingle();
            movies[0].Id.Should().Be(deleted.Id);
            movies[0].MovieMetadata.Value.ForeignId.Should().Be("metadata-2");
        }

        private Movie GivenItem(string foreignId, MovieStatusType status)
        {
            var metadata = Db.Insert(Builder<MovieMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.ForeignId = foreignId)
                .With(m => m.Status = status)
                .BuildNew());

            var movie = Builder<Movie>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.MovieMetadataId = metadata.Id)
                .With(m => m.Path = "/media/movies/" + foreignId)
                .BuildNew();

            return Subject.Insert(movie);
        }
    }
}
