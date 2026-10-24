using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
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

            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.GetItemsTouchingFolder(It.IsAny<string>()))
                  .Returns((string folder) => new List<MoviePathEntry>
                  {
                      new() { Id = 1, Path = _folder, ItemType = ItemType.Scene },
                      new() { Id = 2, Path = _folder, ItemType = ItemType.Scene },
                      new() { Id = 9, Path = _movieFolder, ItemType = ItemType.Movie },
                      new() { Id = 50, Path = @"C:\lib\scenes\Other Studio".AsOsAgnostic(), ItemType = ItemType.Scene }
                  }.Where(e => e.Path.PathEquals(folder) || folder.IsParentPath(e.Path) || e.Path.IsParentPath(folder)).ToList());

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
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

            Subject.FilterFilesTrackedByAnyItem(_folder, files)
                   .Should().BeEquivalentTo(
                       System.IO.Path.Combine(_movieFolder, "movie-extra.mp4"),
                       System.IO.Path.Combine(_folder, "s2.mp4"));
        }

        [Test]
        public void should_only_read_rows_of_items_in_the_folder_never_the_whole_table()
        {
            Subject.FilterFilesTrackedByAnyItem(_folder, new List<string> { System.IO.Path.Combine(_folder, "s1.mp4") });

            Mocker.GetMock<IMediaFileRepository>()
                  .Verify(v => v.GetFilesByMovies(It.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2, 9 }))), Times.Once());
            Mocker.GetMock<IMediaFileRepository>()
                  .Verify(v => v.GetAllFiles(), Times.Never());
        }

        [Test]
        public void should_take_item_paths_from_the_layout_cache_never_the_movie_table()
        {
            Subject.FilterFilesTrackedByAnyItem(_folder, new List<string> { System.IO.Path.Combine(_folder, "s1.mp4") });
            Subject.FilterFilesTrackedByAnyItem(_folder, new List<string> { System.IO.Path.Combine(_folder, "s1.mp4") });

            Mocker.GetMock<IMovieRepository>().Verify(v => v.AllMoviePathEntries(), Times.Never());
            Mocker.GetMock<ISceneFolderLayout>().Verify(v => v.GetItemsTouchingFolder(_folder), Times.Exactly(2));
        }

        [Test]
        public void should_read_no_rows_when_no_item_is_in_the_folder()
        {
            var files = new List<string> { System.IO.Path.Combine(@"C:\lib\elsewhere".AsOsAgnostic(), "a.mp4") };

            Subject.FilterFilesTrackedByAnyItem(@"C:\lib\elsewhere".AsOsAgnostic(), files).Should().BeEquivalentTo(files);

            Mocker.GetMock<IMediaFileRepository>()
                  .Verify(v => v.GetFilesByMovies(It.IsAny<IEnumerable<int>>()), Times.Never());
        }
    }
}
