using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Consumers.Kometa;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Metadata.Consumers.Kometa
{
    [TestFixture]
    public class SharedFolderFixture : CoreTest<KometaMetadata>
    {
        private Movie _movie;
        private MovieFile _movieFile;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new MetadataDefinition
            {
                Settings = new KometaMetadataSettings { MovieImages = true }
            };

            _movie = new Movie
            {
                Id = 1,
                Path = @"C:\Test\Scenes\Bratty Sis".AsOsAgnostic(),
                MovieMetadata = new MovieMetadata
                {
                    Title = "Scene",
                    Images = new List<MediaCover.MediaCover> { new() { CoverType = MediaCoverTypes.Poster } }
                }
            };

            _movieFile = new MovieFile { RelativePath = "Bratty Sis - Scene (2026).mkv" };

            Mocker.GetMock<IMapCoversToLocal>()
                  .Setup(s => s.GetMovieCoverPath(It.IsAny<int>(), It.IsAny<MediaCoverTypes>()))
                  .Returns("/covers/poster.jpg");
        }

        [Test]
        public void should_skip_folder_level_images_in_a_shared_folder()
        {
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);

            Subject.MovieImages(_movie, _movieFile).Should().BeEmpty();
        }

        [Test]
        public void should_write_poster_in_an_exclusive_folder()
        {
            Subject.MovieImages(_movie, _movieFile).Should().ContainSingle()
                   .Which.RelativePath.Should().Be("poster.jpg");
        }
    }
}
