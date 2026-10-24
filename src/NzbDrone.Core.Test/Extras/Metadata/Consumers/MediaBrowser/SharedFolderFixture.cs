using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Consumers.MediaBrowser;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Metadata.Consumers.MediaBrowser
{
    [TestFixture]
    public class SharedFolderFixture : CoreTest<MediaBrowserMetadata>
    {
        private Movie _movie;
        private MovieFile _movieFile;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new MetadataDefinition
            {
                Settings = new MediaBrowserMetadataSettings { MovieMetadata = true }
            };

            _movie = new Movie
            {
                Id = 1,
                Path = @"C:\Test\Scenes\Bratty Sis".AsOsAgnostic(),
                MovieMetadata = new MovieMetadata { Title = "Scene", ImdbId = "tt0000001" }
            };

            _movieFile = new MovieFile { RelativePath = "Bratty Sis - Scene (2026).mkv" };
        }

        [Test]
        public void should_skip_folder_level_movie_xml_in_a_shared_folder()
        {
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);

            Subject.MovieMetadata(_movie, _movieFile).Should().BeNull();
        }

        [Test]
        public void should_write_movie_xml_in_an_exclusive_folder()
        {
            Subject.MovieMetadata(_movie, _movieFile).RelativePath.Should().Be("movie.xml");
        }
    }
}
