using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Files
{
    [TestFixture]
    public class ExtraFileServiceFixture : CoreTest<SubtitleFileService>
    {
        private const string MoviePath = "/lib/scenes/Shady Spa";

        private Movie _movie;
        private MovieFile _movieFile;
        private SubtitleFile _subtitle;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 1)
                                   .With(m => m.Path = MoviePath)
                                   .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                           .With(f => f.Id = 10)
                                           .With(f => f.MovieId = _movie.Id)
                                           .Build();

            _subtitle = new SubtitleFile { Id = 100, MovieId = _movie.Id, MovieFileId = _movieFile.Id, RelativePath = "scene.en.srt" };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_movie.Id))
                  .Returns(_movie);

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovieFile(_movieFile.Id))
                  .Returns(new List<SubtitleFile> { _subtitle });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(MoviePath))
                  .Returns("/lib/scenes");

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(MoviePath + "/scene.en.srt"))
                  .Returns(MoviePath);
        }

        private void Delete(DeleteMediaFileReason reason)
        {
            Subject.HandleAsync(new MovieFileDeletedEvent(_movieFile, reason));
        }

        [Test]
        public void should_not_recycle_extras_when_reason_is_missing_from_disk()
        {
            Delete(DeleteMediaFileReason.MissingFromDisk);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovieFile(_movieFile.Id), Times.Once());
        }

        [Test]
        public void should_recycle_extras_when_reason_is_manual()
        {
            Delete(DeleteMediaFileReason.Manual);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovieFile(_movieFile.Id), Times.Once());
        }

        [Test]
        public void should_recycle_extras_when_reason_is_upgrade()
        {
            Delete(DeleteMediaFileReason.Upgrade);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovieFile(_movieFile.Id), Times.Once());
        }
    }
}
