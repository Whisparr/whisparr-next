using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupOrphanedMovieFilesFixture : DbTest<CleanupOrphanedMovieFiles, MovieFile>
    {
        private MovieFile GivenFile(int movieId)
        {
            var movieFile = Builder<MovieFile>.CreateNew()
                                              .With(f => f.MovieId = movieId)
                                              .With(f => f.Quality = new QualityModel())
                                              .With(f => f.Languages = new List<Language>())
                                              .BuildNew();

            return Db.Insert(movieFile);
        }

        private Movie GivenMovie()
        {
            return Db.Insert(Builder<Movie>.CreateNew().BuildNew());
        }

        [Test]
        public void should_delete_a_file_whose_movie_is_gone()
        {
            GivenFile(6814);

            Subject.Clean();

            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_keep_an_unmapped_file()
        {
            GivenFile(0);

            Subject.Clean();

            AllStoredModels.Should().HaveCount(1);
        }

        [Test]
        public void should_keep_every_file_of_a_movie_including_ones_it_does_not_point_at()
        {
            var movie = GivenMovie();
            GivenFile(movie.Id);
            GivenFile(movie.Id);

            Subject.Clean();

            AllStoredModels.Should().HaveCount(2);
        }

        [Test]
        public void should_delete_only_the_orphans()
        {
            var movie = GivenMovie();
            var kept = GivenFile(movie.Id);
            GivenFile(movie.Id + 100);

            Subject.Clean();

            AllStoredModels.Select(f => f.Id).Should().Equal(kept.Id);
        }
    }
}
