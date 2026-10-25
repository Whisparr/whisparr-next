using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Consumers.Kometa;
using NzbDrone.Core.Extras.Metadata.Consumers.Xbmc;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.MediaCover;
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
            files.Single().MovieFileId.Should().BeNull();
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

        [Test]
        public void organise_to_shared_folder_recycles_tracked_kometa_poster()
        {
            var oldFolder = @"C:\Test\Scenes\Old Scene Folder".AsOsAgnostic();
            var poster = new MetadataFile
            {
                Id = 5,
                MovieId = 1,
                Consumer = nameof(KometaMetadata),
                Type = MetadataType.MovieImage,
                RelativePath = "poster.jpg"
            };

            var consumer = Mocker.Resolve<KometaMetadata>();
            consumer.Definition = new MetadataDefinition { Settings = new KometaMetadataSettings { MovieImages = true } };

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);
            Mocker.GetMock<IMetadataFactory>().Setup(f => f.GetAvailableProviders()).Returns(new List<IMetadata> { consumer });
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MetadataFile> { poster });
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(Path.Combine(oldFolder, "poster.jpg"))).Returns(true);

            Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { RenamedFromOldFolder(oldFolder) });

            Mocker.GetMock<IRecycleBinProvider>().Verify(r => r.DeleteFile(Path.Combine(oldFolder, "poster.jpg"), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IMetadataFileService>().Verify(s => s.Delete(5), Times.Once());
        }

        [Test]
        public void organise_to_shared_folder_skips_tracked_poster_when_target_exists()
        {
            var oldFolder = @"C:\Test\Scenes\Old Scene Folder".AsOsAgnostic();
            var poster = new MetadataFile
            {
                MovieId = 1,
                Consumer = nameof(XbmcMetadata),
                Type = MetadataType.MovieImage,
                RelativePath = "poster.jpg"
            };

            GivenXbmcPosterRename(poster);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(Path.Combine(_movie.Path, "Bratty Sis - Scene (2026)-poster.jpg")))
                  .Returns(true);

            var moved = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { RenamedFromOldFolder(oldFolder) });

            moved.Should().BeEmpty();
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void organise_to_shared_folder_moves_tracked_folder_level_poster()
        {
            var oldFolder = @"C:\Test\Scenes\Old Scene Folder".AsOsAgnostic();
            var movieFile = new MovieFile { Id = 10, RelativePath = "Bratty Sis - Scene (2026).mkv" };
            var poster = new MetadataFile
            {
                MovieId = 1,
                Consumer = nameof(XbmcMetadata),
                Type = MetadataType.MovieImage,
                RelativePath = "poster.jpg"
            };

            _movie.MovieMetadata.Value.Images = new List<MediaCover.MediaCover>
            {
                new() { CoverType = MediaCoverTypes.Poster },
                new() { CoverType = MediaCoverTypes.Fanart }
            };

            var consumer = Mocker.Resolve<XbmcMetadata>();
            consumer.Definition = new MetadataDefinition { Settings = new XbmcMetadataSettings { MovieImages = true } };

            Mocker.GetMock<IMapCoversToLocal>()
                  .Setup(s => s.GetMovieCoverPath(It.IsAny<int>(), It.IsAny<MediaCoverTypes>(), It.IsAny<int?>()))
                  .Returns<int, MediaCoverTypes, int?>((_, type, _) => $"/covers/{type.ToString().ToLowerInvariant()}.jpg");
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);
            Mocker.GetMock<IMetadataFactory>().Setup(f => f.GetAvailableProviders()).Returns(new List<IMetadata> { consumer });
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MetadataFile> { poster });

            var renamed = new RenamedMovieFile
            {
                MovieFile = movieFile,
                PreviousPath = Path.Combine(oldFolder, "Old Name.mkv"),
                PreviousRelativePath = "Old Name.mkv"
            };

            var moved = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            Mocker.GetMock<IDiskProvider>().Verify(
                d => d.MoveFile(Path.Combine(oldFolder, "poster.jpg"), Path.Combine(_movie.Path, "Bratty Sis - Scene (2026)-poster.jpg"), false),
                Times.Once());
            var row = moved.Cast<MetadataFile>().Single();
            row.RelativePath.Should().Be("Bratty Sis - Scene (2026)-poster.jpg");
            row.MovieFileId.Should().Be(10);
        }

        private static RenamedMovieFile RenamedFromOldFolder(string oldFolder)
        {
            return new RenamedMovieFile
            {
                MovieFile = new MovieFile { Id = 10, RelativePath = "Bratty Sis - Scene (2026).mkv" },
                PreviousPath = Path.Combine(oldFolder, "Old Name.mkv"),
                PreviousRelativePath = "Old Name.mkv"
            };
        }

        private void GivenXbmcPosterRename(MetadataFile poster)
        {
            _movie.MovieMetadata.Value.Images = new List<MediaCover.MediaCover> { new() { CoverType = MediaCoverTypes.Poster } };

            var consumer = Mocker.Resolve<XbmcMetadata>();
            consumer.Definition = new MetadataDefinition { Settings = new XbmcMetadataSettings { MovieImages = true } };

            Mocker.GetMock<IMapCoversToLocal>()
                  .Setup(s => s.GetMovieCoverPath(It.IsAny<int>(), It.IsAny<MediaCoverTypes>(), It.IsAny<int?>()))
                  .Returns("/covers/poster.jpg");
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);
            Mocker.GetMock<IMetadataFactory>().Setup(f => f.GetAvailableProviders()).Returns(new List<IMetadata> { consumer });
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MetadataFile> { poster });
        }
    }
}
