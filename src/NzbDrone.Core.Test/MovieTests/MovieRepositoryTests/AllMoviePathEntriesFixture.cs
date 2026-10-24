using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieRepositoryTests
{
    [TestFixture]
    public class AllMoviePathEntriesFixture : DbTest<MovieRepository, Movie>
    {
        [Test]
        public void should_return_item_type_per_movie()
        {
            var scene = GivenItem("metadata-1", "/media/scenes/Studio", ItemType.Scene);
            var movie = GivenItem("metadata-2", "/media/movies/A", ItemType.Movie);

            var entries = Subject.AllMoviePathEntries();

            entries.Should().HaveCount(2);
            entries.Single(e => e.Id == scene.Id).ItemType.Should().Be(ItemType.Scene);
            entries.Single(e => e.Id == scene.Id).Path.Should().Be("/media/scenes/Studio");
            entries.Single(e => e.Id == movie.Id).ItemType.Should().Be(ItemType.Movie);
            entries.Single(e => e.Id == movie.Id).Path.Should().Be("/media/movies/A");
        }

        private Movie GivenItem(string foreignId, string path, ItemType itemType)
        {
            var metadata = Db.Insert(Builder<MovieMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.ForeignId = foreignId)
                .With(m => m.ItemType = itemType)
                .BuildNew());

            var movie = Builder<Movie>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.MovieMetadataId = metadata.Id)
                .With(m => m.Path = path)
                .BuildNew();

            return Subject.Insert(movie);
        }
    }
}
