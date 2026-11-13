using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileServiceTests
{
    [TestFixture]
    public class FilterFixture : CoreTest<MediaFileService>
    {
        private Movie _series;

        [SetUp]
        public void Setup()
        {
            _series = new Movie
            {
                Id = 10,
                Path = @"C:\".AsOsAgnostic()
            };
        }

        [Test]
        public void filter_should_return_all_files_if_no_existing_files()
        {
            var files = new List<string>()
            {
                "C:\\file1.avi".AsOsAgnostic(),
                "C:\\file2.avi".AsOsAgnostic(),
                "C:\\file3.avi".AsOsAgnostic()
            };

            Mocker.GetMock<IMediaFileRepository>()
                .Setup(c => c.GetFilesByMovie(It.IsAny<int>()))
                .Returns(new List<MovieFile>());

            Subject.FilterExistingFiles(files, _series).Should().BeEquivalentTo(files);
        }

        [Test]
        public void filter_should_return_none_if_all_files_exist()
        {
            var files = new List<string>()
            {
                "C:\\file1.avi".AsOsAgnostic(),
                "C:\\file2.avi".AsOsAgnostic(),
                "C:\\file3.avi".AsOsAgnostic()
            };

            Mocker.GetMock<IMediaFileRepository>()
                .Setup(c => c.GetFilesByMovie(It.IsAny<int>()))
                .Returns(files.Select(f => new MovieFile { RelativePath = Path.GetFileName(f) }).ToList());

            Subject.FilterExistingFiles(files, _series).Should().BeEmpty();
        }

        [Test]
        public void filter_should_return_none_existing_files()
        {
            var files = new List<string>()
            {
                "C:\\file1.avi".AsOsAgnostic(),
                "C:\\file2.avi".AsOsAgnostic(),
                "C:\\file3.avi".AsOsAgnostic()
            };

            Mocker.GetMock<IMediaFileRepository>()
                .Setup(c => c.GetFilesByMovie(It.IsAny<int>()))
                .Returns(new List<MovieFile>
                {
                    new MovieFile { RelativePath = "file2.avi".AsOsAgnostic() }
                });

            Subject.FilterExistingFiles(files, _series).Should().HaveCount(2);
            Subject.FilterExistingFiles(files, _series).Should().NotContain("C:\\file2.avi".AsOsAgnostic());
        }

        [Test]
        public void filter_should_return_none_existing_files_ignoring_case()
        {
            WindowsOnly();

            var files = new List<string>()
            {
                "C:\\file1.avi".AsOsAgnostic(),
                "C:\\FILE2.avi".AsOsAgnostic(),
                "C:\\file3.avi".AsOsAgnostic()
            };

            Mocker.GetMock<IMediaFileRepository>()
                .Setup(c => c.GetFilesByMovie(It.IsAny<int>()))
                .Returns(new List<MovieFile>
                {
                    new MovieFile { RelativePath = "file2.avi".AsOsAgnostic() }
                });

            Subject.FilterExistingFiles(files, _series).Should().HaveCount(2);
            Subject.FilterExistingFiles(files, _series).Should().NotContain("C:\\file2.avi".AsOsAgnostic());
        }

        [Test]
        public void filter_should_return_none_existing_files_not_ignoring_case()
        {
            PosixOnly();

            var files = new List<string>()
            {
                "C:\\file1.avi".AsOsAgnostic(),
                "C:\\FILE2.avi".AsOsAgnostic(),
                "C:\\file3.avi".AsOsAgnostic()
            };

            Mocker.GetMock<IMediaFileRepository>()
                .Setup(c => c.GetFilesByMovie(It.IsAny<int>()))
                .Returns(new List<MovieFile>
                {
                    new MovieFile { RelativePath = "file2.avi".AsOsAgnostic() }
                });

            Subject.FilterExistingFiles(files, _series).Should().HaveCount(3);
        }

        [Test]
        public void filter_should_not_change_casing()
        {
            var files = new List<string>()
            {
                "C:\\FILE1.avi".AsOsAgnostic()
            };

            Mocker.GetMock<IMediaFileRepository>()
                .Setup(c => c.GetFilesByMovie(It.IsAny<int>()))
                .Returns(new List<MovieFile>());

            Subject.FilterExistingFiles(files, _series).Should().HaveCount(1);
            Subject.FilterExistingFiles(files, _series).Should().NotContain(files.First().ToLower());
            Subject.FilterExistingFiles(files, _series).Should().Contain(files.First());
        }

        [Test]
        public void filter_all_should_drop_every_tracked_and_unmapped_file_without_a_query_per_file()
        {
            var scenePath = @"C:\scenes".AsOsAgnostic();
            var primary = Path.Combine(scenePath, "primary.mp4");
            var second = Path.Combine(scenePath, "second.mp4");
            var unmapped = @"C:\import\unmapped.mp4".AsOsAgnostic();
            var fresh = @"C:\import\fresh.mp4".AsOsAgnostic();

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string> { { 1, scenePath } });
            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetAllFiles())
                  .Returns(new List<MovieFile>
                  {
                      new() { MovieId = 1, RelativePath = "primary.mp4" },
                      new() { MovieId = 1, RelativePath = "second.mp4" },
                      new() { MovieId = 0, OriginalFilePath = unmapped },
                      new() { MovieId = 99, RelativePath = "orphan.mp4" }
                  });

            Subject.FilterExistingFiles(new List<string> { primary, second, unmapped, fresh })
                   .Should().BeEquivalentTo(new[] { fresh });

            Mocker.GetMock<IMovieRepository>().Verify(v => v.Get(It.IsAny<int>()), Times.Never());
        }
    }
}
