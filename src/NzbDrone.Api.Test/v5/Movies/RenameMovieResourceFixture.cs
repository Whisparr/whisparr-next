using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using Whisparr.Api.V5.Movies;

namespace NzbDrone.Api.Test.v5.Movies
{
    [TestFixture]
    public class RenameMovieResourceFixture
    {
        [Test]
        public void should_use_root_relative_paths_when_the_folder_changes()
        {
            var preview = new RenameMovieFilePreview
            {
                MovieId = 7,
                MovieFileId = 70,
                ExistingPath = "s1.mp4",
                NewPath = "../Buffy - 2020.mp4",
                ExistingRootRelativePath = "scenes/Shady Spa/2020 - Buffy [id]/s1.mp4",
                NewRootRelativePath = "scenes/Shady Spa/Buffy - 2020.mp4"
            };

            var resource = preview.ToResource();

            resource.ExistingPath.Should().Be("scenes/Shady Spa/2020 - Buffy [id]/s1.mp4");
            resource.NewPath.Should().Be("scenes/Shady Spa/Buffy - 2020.mp4");
        }

        [Test]
        public void should_keep_movie_relative_paths_when_the_folder_is_unchanged()
        {
            var preview = new RenameMovieFilePreview
            {
                MovieId = 7,
                MovieFileId = 70,
                ExistingPath = "s1.mp4",
                NewPath = "Buffy - 2020.mp4"
            };

            var resource = preview.ToResource();

            resource.ExistingPath.Should().Be("s1.mp4");
            resource.NewPath.Should().Be("Buffy - 2020.mp4");
        }
    }
}
