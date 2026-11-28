using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Movies;

namespace NzbDrone.Api.Test.v5.Movies
{
    [TestFixture]
    public class MovieEditorControllerFixture : TestBase<MovieEditorController>
    {
        private Movie _covered;
        private Movie _uncovered;

        [SetUp]
        public void Setup()
        {
            _covered = GivenMovie(1, true);
            _uncovered = GivenMovie(2, false);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<List<int>>()))
                  .Returns(new List<Movie> { _covered, _uncovered });
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.UpdateMovie(It.IsAny<List<Movie>>(), It.IsAny<bool>()))
                  .Returns<List<Movie>, bool>((movies, _) => movies);
            Mocker.GetMock<IQualityProfileService>().Setup(s => s.Exists(1)).Returns(true);

            Mocker.SetConstant(new MovieEditorValidator(
                new RootFolderExistsValidator<Movie>(Mocker.Resolve<IRootFolderService>()),
                new QualityProfileExistsValidator<Movie>(Mocker.Resolve<IQualityProfileService>())));
        }

        [Test]
        public void bulk_edit_without_cover_changes_queues_nothing()
        {
            Subject.SaveAll(new MovieEditorResource { MovieIds = new List<int> { 1, 2 }, Monitored = true });

            Mocker.GetMock<IQueueNewMovieCovers>()
                  .Verify(v => v.QueueNewMovieCovers(It.IsAny<IEnumerable<int>>()), Times.Never());
            Mocker.GetMock<IMapCoversToLocal>()
                  .Verify(v => v.DeleteMovieCovers(It.IsAny<int>(), It.IsAny<IEnumerable<MediaCoverTypes>>()), Times.Never());
        }

        [Test]
        public void bulk_edit_disabling_covers_purges_only_changed_movies()
        {
            Subject.SaveAll(new MovieEditorResource { MovieIds = new List<int> { 1, 2 }, DownloadCovers = false });

            Mocker.GetMock<IMapCoversToLocal>()
                  .Verify(v => v.DeleteMovieCovers(1, It.IsAny<IEnumerable<MediaCoverTypes>>()), Times.Once());
            Mocker.GetMock<IMapCoversToLocal>()
                  .Verify(v => v.DeleteMovieCovers(It.IsAny<int>(), It.IsAny<IEnumerable<MediaCoverTypes>>()), Times.Once());
            Mocker.GetMock<IQueueNewMovieCovers>()
                  .Verify(v => v.QueueNewMovieCovers(It.IsAny<IEnumerable<int>>()), Times.Never());
        }

        [Test]
        public void bulk_edit_enabling_covers_queues_only_changed_movies()
        {
            Subject.SaveAll(new MovieEditorResource { MovieIds = new List<int> { 1, 2 }, DownloadCovers = true });

            Mocker.GetMock<IQueueNewMovieCovers>()
                  .Verify(v => v.QueueNewMovieCovers(It.Is<IEnumerable<int>>(ids => new List<int>(ids).Count == 1)), Times.Once());
            Mocker.GetMock<IMapCoversToLocal>()
                  .Verify(v => v.DeleteMovieCovers(It.IsAny<int>(), It.IsAny<IEnumerable<MediaCoverTypes>>()), Times.Never());
        }

        private static Movie GivenMovie(int id, bool downloadCovers)
        {
            return new Movie
            {
                Id = id,
                Monitored = true,
                DownloadCovers = downloadCovers,
                QualityProfileId = 1,
                Tags = new HashSet<int>(),
                MovieMetadata = new MovieMetadata
                {
                    Title = "Movie " + id,
                    Images = new List<MediaCover>()
                }
            };
        }
    }
}
