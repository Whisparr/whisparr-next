using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MoveMovieServiceFixture : CoreTest<MoveMovieService>
    {
        private Movie _movie;
        private MoveMovieCommand _command;
        private BulkMoveMovieCommand _bulkCommand;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>
                .CreateNew()
                .Build();

            _command = new MoveMovieCommand
            {
                MovieId = 1,
                SourcePath = @"C:\Test\Movies\Movie".AsOsAgnostic(),
                DestinationPath = @"C:\Test\Movies2\Movie".AsOsAgnostic()
            };

            _bulkCommand = new BulkMoveMovieCommand
            {
                Movies = new List<BulkMoveMovie>
                                    {
                                        new BulkMoveMovie
                                        {
                                            MovieId = 1,
                                            SourcePath = @"C:\Test\Movies\Movie".AsOsAgnostic()
                                        }
                                    },
                DestinationRootFolder = @"C:\Test\Movies2".AsOsAgnostic()
            };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(It.IsAny<int>()))
                  .Returns(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(true);
        }

        private void GivenFailedMove()
        {
            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFolder(It.IsAny<string>(), It.IsAny<string>(), TransferMode.Move))
                  .Throws<IOException>();
        }

        [Test]
        public void should_log_error_when_move_throws_an_exception()
        {
            GivenFailedMove();

            Subject.Execute(_command);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_revert_movie_path_on_error()
        {
            GivenFailedMove();

            Subject.Execute(_command);

            ExceptionVerification.ExpectedErrors(1);

            Mocker.GetMock<IMovieService>()
                  .Verify(v => v.UpdateMovie(It.IsAny<Movie>()), Times.Once());
        }

        [Test]
        public void should_use_destination_path()
        {
            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_command.SourcePath, _command.DestinationPath, TransferMode.Move), Times.Once());

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(v => v.GetMovieFolder(It.IsAny<Movie>(), null), Times.Never());
        }

        [Test]
        public void should_build_new_path_when_root_folder_is_provided()
        {
            var movieFolder = "Movie";
            var expectedPath = Path.Combine(_bulkCommand.DestinationRootFolder, movieFolder);

            Mocker.GetMock<IBuildFileNames>()
                    .Setup(s => s.GetMovieFolder(It.IsAny<Movie>(), null))
                    .Returns(movieFolder);

            Subject.Execute(_bulkCommand);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_bulkCommand.Movies.First().SourcePath, expectedPath, TransferMode.Move), Times.Once());
        }

        private void GivenSharedSceneWithFiles()
        {
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.IsShared(It.IsAny<Movie>()))
                  .Returns(true);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MovieFile> { new MovieFile { RelativePath = "scene one.mp4" } });

            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<OtherExtraFile> { new OtherExtraFile { RelativePath = "scene one.jpg" } });

            Mocker.GetMock<ISubtitleFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<SubtitleFile>());

            Mocker.GetMock<IMetadataFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MetadataFile>());

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns<string>(p => p.StartsWith(_command.SourcePath));
        }

        [Test]
        public void moving_scene_in_shared_folder_moves_owned_files_one_by_one()
        {
            GivenSharedSceneWithFiles();
            _movie.Path = _command.DestinationPath;

            Subject.Execute(_command);

            _movie.Path.Should().Be(_command.DestinationPath);

            var transfer = Mocker.GetMock<IDiskTransferService>();

            transfer.Verify(v => v.TransferFile(Path.Combine(_command.SourcePath, "scene one.mp4"), Path.Combine(_command.DestinationPath, "scene one.mp4"), TransferMode.Move, false), Times.Once());
            transfer.Verify(v => v.TransferFile(Path.Combine(_command.SourcePath, "scene one.jpg"), Path.Combine(_command.DestinationPath, "scene one.jpg"), TransferMode.Move, false), Times.Once());
            transfer.Verify(v => v.TransferFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Exactly(2));
            transfer.Verify(v => v.TransferFolder(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>()), Times.Never());
        }

        [Test]
        public void moving_scene_in_shared_folder_does_not_overwrite_existing_destination_file()
        {
            GivenSharedSceneWithFiles();

            var existing = Path.Combine(_command.DestinationPath, "scene one.mp4");

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns<string>(p => p.StartsWith(_command.SourcePath) || p == existing);

            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(It.IsAny<string>(), existing, It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void moving_scene_in_exclusive_folder_still_moves_folder()
        {
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.IsShared(It.IsAny<Movie>()))
                  .Returns(false);

            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_command.SourcePath, _command.DestinationPath, TransferMode.Move), Times.Once());
            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_skip_movie_folder_if_it_does_not_exist()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.Execute(_command);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFolder(_command.SourcePath, _command.DestinationPath, TransferMode.Move), Times.Never());

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(v => v.GetMovieFolder(It.IsAny<Movie>(), null), Times.Never());
        }
    }
}
