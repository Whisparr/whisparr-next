using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Consumers.Xbmc;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Metadata
{
    [TestFixture]
    public class MetadataServiceFixture : CoreTest<MetadataService>
    {
        private Movie _movie;
        private List<MovieFile> _movieFiles;
        private XbmcMetadata _consumer;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                Path = @"C:\Test\Scenes\Bratty Sis".AsOsAgnostic(),
                MovieMetadata = new MovieMetadata { Title = "Scene" }
            };

            _movieFiles = new List<MovieFile>
            {
                new() { Id = 10, RelativePath = "A.mkv" },
                new() { Id = 11, RelativePath = "B.mkv" }
            };

            var consumer = new Mock<IMetadata>();
            consumer.Setup(c => c.MovieImages(It.IsAny<Movie>(), It.IsAny<MovieFile>()))
                    .Returns<Movie, MovieFile>((_, f) => new List<ImageFileResult>
                    {
                        new((f?.RelativePath ?? "folder") + "-poster.jpg", "/covers/poster.jpg")
                    });

            Mocker.GetMock<IMetadataFactory>()
                  .Setup(f => f.Enabled())
                  .Returns(new List<IMetadata> { consumer.Object });

            Mocker.GetMock<IMetadataFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MetadataFile>());

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(_movieFiles);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(true);
        }

        [Test]
        public void should_write_images_per_file_in_a_shared_folder()
        {
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);

            var files = Subject.CreateAfterMediaCoverUpdate(_movie).Cast<MetadataFile>().ToList();

            files.Select(f => f.RelativePath).Should().BeEquivalentTo("A.mkv-poster.jpg", "B.mkv-poster.jpg");
            files.Select(f => f.MovieFileId).Should().BeEquivalentTo(new[] { 10, 11 });
        }

        [Test]
        public void should_write_one_folder_level_image_set_in_an_exclusive_folder()
        {
            var files = Subject.CreateAfterMediaCoverUpdate(_movie).Cast<MetadataFile>().ToList();

            files.Should().ContainSingle().Which.RelativePath.Should().Be("A.mkv-poster.jpg");
            files.Single().MovieFileId.Should().Be(0);
        }

        [Test]
        public void should_move_tracked_movie_nfo_to_per_file_nfo_on_rename_into_a_shared_folder()
        {
            var oldFolder = @"C:\Test\Scenes\Old Scene Folder".AsOsAgnostic();
            var movieFile = new MovieFile { Id = 10, RelativePath = "Bratty Sis - Scene (2026).mkv" };
            var tracked = new MetadataFile
            {
                MovieId = 1,
                MovieFileId = 10,
                Consumer = nameof(XbmcMetadata),
                Type = MetadataType.MovieMetadata,
                RelativePath = "movie.nfo"
            };

            _consumer = Mocker.Resolve<XbmcMetadata>();
            _consumer.Definition = new MetadataDefinition { Settings = new XbmcMetadataSettings { UseMovieNfo = true } };

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);
            Mocker.GetMock<IMetadataFactory>().Setup(f => f.GetAvailableProviders()).Returns(new List<IMetadata> { _consumer });
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MetadataFile> { tracked });

            var renamed = new RenamedMovieFile
            {
                MovieFile = movieFile,
                PreviousPath = Path.Combine(oldFolder, "Old Name.mkv"),
                PreviousRelativePath = "Old Name.mkv"
            };

            var moved = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            var expected = Path.Combine(_movie.Path, "Bratty Sis - Scene (2026).nfo");

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(Path.Combine(oldFolder, "movie.nfo"), expected, false), Times.Once());
            moved.Cast<MetadataFile>().Single().RelativePath.Should().Be("Bratty Sis - Scene (2026).nfo");
        }
    }
}
