using System;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieFileMovingServiceTests
{
    [TestFixture]
    public class MoveMovieFileFixture : CoreTest<MovieFileMovingService>
    {
        private Movie _movie;
        private MovieFile _movieFile;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                     .With(s => s.Path = @"C:\Test\Movies\Movie".AsOsAgnostic())
                                     .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                               .With(f => f.Path = null)
                                               .With(f => f.RelativePath = @"File.avi")
                                               .Build();

            _localMovie = Builder<LocalMovie>.CreateNew()
                                                 .With(l => l.Movie = _movie)
                                                 .Build();

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildFileName(It.IsAny<Movie>(), It.IsAny<MovieFile>(), null, null, false))
                  .Returns("File Name");

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildFilePath(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(@"C:\Test\Movies\Movie\File Name.avi".AsOsAgnostic());

            var rootFolder = @"C:\Test\Movies\".AsOsAgnostic();

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), null))
                .Returns(rootFolder);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(rootFolder))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);
        }

        [Test]
        public void should_catch_UnauthorizedAccessException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<UnauthorizedAccessException>();

            Subject.MoveMovieFile(_movieFile, _localMovie);
        }

        [Test]
        public void should_catch_InvalidOperationException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<InvalidOperationException>();

            Subject.MoveMovieFile(_movieFile, _localMovie);
        }

        [Test]
        public void should_notify_on_movie_folder_creation()
        {
            Subject.MoveMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<MovieFolderCreatedEvent>(It.Is<MovieFolderCreatedEvent>(p =>
                      p.MovieFolder.IsNotNullOrWhiteSpace())),
                      Times.Once());
        }

        private string GivenFlatStudioFolder()
        {
            var studioFolder = @"C:\Test\Movies\Shady Spa".AsOsAgnostic();
            var destination = System.IO.Path.Combine(studioFolder, "Buffy - 2020.mp4");

            _movie.Path = studioFolder;
            _movieFile.RelativePath = "incoming.mp4";

            Mocker.GetMock<IBuildMoviePaths>()
                  .Setup(s => s.BuildPath(It.IsAny<Movie>(), false))
                  .Returns(studioFolder);

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildFilePath(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(destination);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(studioFolder))
                  .Returns(true);

            Mocker.GetMock<IImportScript>()
                  .Setup(s => s.TryImport(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LocalMovie>(), It.IsAny<MovieFile>(), It.IsAny<TransferMode>()))
                  .Returns(ScriptImportDecision.DeferMove);

            return destination;
        }

        [Test]
        public void import_into_flat_folder_with_siblings_places_file_in_studio_folder()
        {
            var destination = GivenFlatStudioFolder();

            Subject.MoveMovieFile(_movieFile, _localMovie);

            _movieFile.RelativePath.Should().Be("Buffy - 2020.mp4");

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(It.IsAny<string>(), destination, TransferMode.Move, false), Times.Once());

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Once());

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void import_collision_is_never_transferred_with_overwrite()
        {
            var destination = GivenFlatStudioFolder();

            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(It.IsAny<string>(), destination, TransferMode.Move, false))
                  .Throws(new DestinationAlreadyExistsException("exists"));

            Assert.Throws<DestinationAlreadyExistsException>(() => Subject.MoveMovieFile(_movieFile, _localMovie));

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>(), true), Times.Never());
        }

        [Test]
        public void should_not_notify_if_movie_folder_already_exists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_movie.Path))
                  .Returns(true);

            Subject.MoveMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<MovieFolderCreatedEvent>(It.Is<MovieFolderCreatedEvent>(p =>
                      p.MovieFolder.IsNotNullOrWhiteSpace())),
                      Times.Never());
        }
    }
}
