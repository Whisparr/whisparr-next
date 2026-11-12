using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupExtraFilesInExcludedFoldersFixture : CoreTest<CleanupExtraFilesInExcludedFolders>
    {
        private Movie _movie;
        private List<OtherExtraFile> _extraFiles;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                Path = @"C:\Movies\Some.Movie.2024"
            };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string> { { _movie.Id, _movie.Path } });

            var inBackdrops = new OtherExtraFile
            {
                Id = 1,
                MovieId = _movie.Id,
                RelativePath = @"backdrops\poster.jpg"
            };

            var inSubtitles = new OtherExtraFile
            {
                Id = 2,
                MovieId = _movie.Id,
                RelativePath = @"subtitles\en.srt"
            };

            _extraFiles = new List<OtherExtraFile> { inBackdrops, inSubtitles };

            Mocker.GetMock<IOtherExtraFileRepository>()
                  .Setup(s => s.All())
                  .Returns(_extraFiles);

            // Mirror the production filter: paths under backdrops are excluded.
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns((string basePath, IEnumerable<string> paths, bool filterExtras) =>
                      paths.Where(p => !p.Contains(@"backdrops")).ToList());
        }

        [Test]
        public void should_delete_only_extra_files_in_excluded_folders()
        {
            Subject.Clean();

            Mocker.GetMock<IOtherExtraFileRepository>()
                  .Verify(r => r.DeleteMany(new List<int> { 1 }), Times.Once);
        }

        [Test]
        public void should_not_delete_when_nothing_is_excluded()
        {
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns((string basePath, IEnumerable<string> paths, bool filterExtras) =>
                      paths.ToList());

            Subject.Clean();

            Mocker.GetMock<IOtherExtraFileRepository>()
                  .Verify(r => r.DeleteMany(It.IsAny<List<int>>()), Times.Never);
        }

        [Test]
        public void should_not_load_movies_when_there_are_no_extra_files()
        {
            Mocker.GetMock<IOtherExtraFileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<OtherExtraFile>());

            Subject.Clean();

            Mocker.GetMock<IMovieService>().Verify(s => s.AllMoviePaths(), Times.Never);
        }

        [Test]
        public void should_skip_extra_files_whose_movie_is_gone()
        {
            _extraFiles.Add(new OtherExtraFile { Id = 3, MovieId = 99, RelativePath = @"backdrops\fanart.jpg" });

            Subject.Clean();

            Mocker.GetMock<IOtherExtraFileRepository>()
                  .Verify(r => r.DeleteMany(new List<int> { 1 }), Times.Once);
            Mocker.GetMock<IMovieService>().Verify(s => s.GetAllMovies(), Times.Never);
        }
    }
}
