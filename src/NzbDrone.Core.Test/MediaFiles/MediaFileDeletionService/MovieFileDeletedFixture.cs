using System.IO;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class MovieFileDeletedFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private const string SharedPath = "/lib/scenes/Shady Spa";

        private MovieFile _movieFile;

        [SetUp]
        public void Setup()
        {
            var movie = Builder<Movie>.CreateNew()
                                      .With(m => m.Id = 1)
                                      .With(m => m.Path = SharedPath)
                                      .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                           .With(f => f.RelativePath = "scene one.mp4")
                                           .With(f => f.Path = Path.Combine(SharedPath, "scene one.mp4"))
                                           .With(f => f.Movie = movie)
                                           .Build();

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DeleteEmptyFolders)
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(true);
        }

        [Test]
        public void should_not_sweep_a_folder_that_holds_another_items_folder()
        {
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.HoldsOtherItems(1, SharedPath))
                  .Returns(true);

            Subject.Handle(new MovieFileDeletedEvent(_movieFile, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.RemoveEmptySubfolders(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_sweep_a_folder_that_holds_no_other_items()
        {
            Subject.Handle(new MovieFileDeletedEvent(_movieFile, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.RemoveEmptySubfolders(SharedPath), Times.Once());
        }
    }
}
