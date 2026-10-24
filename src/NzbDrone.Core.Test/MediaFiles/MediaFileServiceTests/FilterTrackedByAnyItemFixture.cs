using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileServiceTests
{
    [TestFixture]
    public class FilterTrackedByAnyItemFixture : CoreTest<MediaFileService>
    {
        private string _folder;
        private string _movieFolder;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            _movieFolder = @"C:\lib\scenes\Shady Spa\Some Movie".AsOsAgnostic();

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.AllMoviePathEntries())
                  .Returns(new List<MoviePathEntry>
                  {
                      new() { Id = 1, Path = _folder, ItemType = ItemType.Scene },
                      new() { Id = 2, Path = _folder, ItemType = ItemType.Scene },
                      new() { Id = 9, Path = _movieFolder, ItemType = ItemType.Movie }
                  });

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetAllFiles())
                  .Returns(new List<MovieFile>
                  {
                      new() { MovieId = 1, RelativePath = "s1.mp4" },
                      new() { MovieId = 9, RelativePath = "movie.mp4" },

                      // Unmapped row: the file stays a candidate
                      new() { MovieId = 0, RelativePath = null, OriginalFilePath = System.IO.Path.Combine(_folder, "s2.mp4") }
                  });
        }

        [Test]
        public void should_drop_files_tracked_by_any_item_but_keep_unmapped_and_untracked()
        {
            var files = new List<string>
            {
                System.IO.Path.Combine(_folder, "s1.mp4"),
                System.IO.Path.Combine(_movieFolder, "movie.mp4"),
                System.IO.Path.Combine(_movieFolder, "movie-extra.mp4"),
                System.IO.Path.Combine(_folder, "s2.mp4")
            };

            Subject.FilterFilesTrackedByAnyItem(files)
                   .Should().BeEquivalentTo(
                       System.IO.Path.Combine(_movieFolder, "movie-extra.mp4"),
                       System.IO.Path.Combine(_folder, "s2.mp4"));
        }
    }
}
