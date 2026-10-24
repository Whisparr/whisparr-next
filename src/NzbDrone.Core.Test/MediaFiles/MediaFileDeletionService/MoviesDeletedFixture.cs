using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class MoviesDeletedFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private const string SharedPath = "/lib/scenes/Shady Spa";

        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 1)
                                   .With(m => m.Path = SharedPath)
                                   .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(SharedPath))
                  .Returns(true);
        }

        private void GivenAllMoviePaths(string otherPath)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string>
                  {
                      { 1, SharedPath },
                      { 2, otherPath }
                  });
        }

        private void Delete()
        {
            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _movie }, true, false));
        }

        [Test]
        public void should_not_recycle_folder_when_another_item_not_in_batch_shares_the_path()
        {
            GivenAllMoviePaths(SharedPath);

            Delete();

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_recycle_folder_when_it_is_parent_of_another_item_not_in_batch()
        {
            GivenAllMoviePaths(SharedPath + "/old scene");

            Delete();

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_recycle_folder_when_no_other_item_shares_it()
        {
            GivenAllMoviePaths("/lib/scenes/Other Studio");

            Delete();

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(SharedPath), Times.Once());
        }
    }
}
