using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Movies;
using Whisparr.Api.V5.Movies;

namespace NzbDrone.Api.Test.v5.Movies
{
    [TestFixture]
    public class MovieResourceFixture
    {
        private static Movie GivenMovie(bool downloadCovers)
        {
            return new Movie
            {
                Id = 1,
                DownloadCovers = downloadCovers,
                MovieMetadata = new MovieMetadata
                {
                    Title = "Some Movie",
                    Images = new List<MediaCover> { new(MediaCoverTypes.Poster, "https://example.com/poster.jpg") }
                }
            };
        }

        [Test]
        public void should_leave_out_the_images_of_a_movie_without_downloads()
        {
            var model = GivenMovie(false);

            model.ToResource(0).Images.Should().BeEmpty();
            model.MovieMetadata.Value.Images.Should().HaveCount(1);
        }

        [Test]
        public void should_keep_the_images_of_a_movie_with_downloads()
        {
            var model = GivenMovie(true);

            model.ToResource(0).Images.Should().HaveCount(1);
        }
    }
}
