using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.Fingerprints.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.Fingerprints
{
    [TestFixture]
    public class FileHashBackfillServiceFixture : CoreTest<FileHashBackfillService>
    {
        private static readonly DateTime LastWrite = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 5, Path = Path.Combine(Path.GetTempPath(), "library", "movie") };

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Movie> { _movie });

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesWithoutOsHash(It.IsAny<int>()))
                .Returns(new List<MovieFile>());

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.GetFilesWithoutOsHash(It.IsAny<int>()))
                .Returns(new List<ImportFile>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileGetLastWrite(It.IsAny<string>()))
                .Returns(LastWrite);

            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputeOsHash(It.IsAny<string>()))
                .Returns<string>(p => "hash-" + Path.GetFileName(p));
        }

        private static List<MovieFile> MovieFiles(int count, int movieId = 5, int firstId = 1)
        {
            return Enumerable.Range(firstId, count)
                .Select(i => new MovieFile { Id = i, MovieId = movieId, RelativePath = $"file{i}.mp4" })
                .ToList();
        }

        private static List<ImportFile> ImportFiles(int count, int firstId = 1)
        {
            return Enumerable.Range(firstId, count)
                .Select(i => new ImportFile { Id = i, Path = Path.Combine(Path.GetTempPath(), "import", $"import{i}.mp4") })
                .ToList();
        }

        [Test]
        public void should_hash_mapped_file_and_save_hash_with_modified_stamp()
        {
            var files = MovieFiles(1);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(files);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().Be("hash-file1.mp4");
            files[0].HashedFileModifiedAt.Should().Be(LastWrite);
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(Path.Combine(_movie.Path, "file1.mp4")), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(files), Times.Once());
        }

        [Test]
        public void should_hash_unmapped_file_by_original_file_path()
        {
            var original = Path.Combine(Path.GetTempPath(), "downloads", "orphan.mp4");
            var files = new List<MovieFile> { new() { Id = 1, MovieId = 0, OriginalFilePath = original } };
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(files);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().Be("hash-orphan.mp4");
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(original), Times.Once());
        }

        [Test]
        public void should_hash_import_files_by_path()
        {
            var files = ImportFiles(2);
            Mocker.GetMock<IImportlFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(files);

            Subject.Execute(new GenerateOsHashesCommand());

            files.Select(f => f.OsHash).Should().Equal("hash-import1.mp4", "hash-import2.mp4");
            files.Should().OnlyContain(f => f.HashedFileModifiedAt == LastWrite);
            Mocker.GetMock<IImportlFileService>().Verify(s => s.UpdateMany(files), Times.Once());
        }

        [Test]
        public void should_hash_at_most_2000_files_per_run_with_movie_files_first()
        {
            var movieFiles = MovieFiles(1500);
            var importFiles = ImportFiles(500);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(movieFiles);
            Mocker.GetMock<IImportlFileService>().Setup(s => s.GetFilesWithoutOsHash(500)).Returns(importFiles);

            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(It.IsAny<string>()), Times.Exactly(2000));
            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesWithoutOsHash(500), Times.Once());
        }

        [Test]
        public void should_not_query_import_files_when_movie_files_use_the_whole_budget()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(MovieFiles(2000));

            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesWithoutOsHash(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(It.IsAny<string>()), Times.Exactly(2000));
        }

        [Test]
        public void should_skip_file_missing_on_disk_without_hashing()
        {
            var files = MovieFiles(2);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(files);
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(Path.Combine(_movie.Path, "file1.mp4")))
                .Returns(false);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().BeNull();
            files[1].OsHash.Should().Be("hash-file2.mp4");
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(Path.Combine(_movie.Path, "file1.mp4")), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.Is<List<MovieFile>>(l => l.Count == 1 && l[0].Id == 2)), Times.Once());
        }

        [Test]
        public void should_skip_mapped_file_whose_movie_is_missing()
        {
            var files = MovieFiles(1, movieId: 99);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(files);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().BeNull();
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_leave_hash_null_on_failure_and_continue()
        {
            var files = MovieFiles(2);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(files);
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputeOsHash(Path.Combine(_movie.Path, "file1.mp4")))
                .Returns((string)null);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().BeNull();
            files[0].HashedFileModifiedAt.Should().BeNull();
            files[1].OsHash.Should().Be("hash-file2.mp4");
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.Is<List<MovieFile>>(l => l.Count == 1 && l[0].Id == 2)), Times.Once());
        }

        [Test]
        public void should_not_save_when_nothing_was_hashed()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(MovieFiles(1));
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns((string)null);

            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.UpdateMany(It.IsAny<List<ImportFile>>()), Times.Never());
        }

        [Test]
        public void should_load_movies_in_a_single_call()
        {
            var files = MovieFiles(3);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(2000)).Returns(files);

            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IMovieService>().Verify(s => s.GetMovies(It.IsAny<IEnumerable<int>>()), Times.Once());
        }

        [Test]
        public void command_should_send_updates_to_client()
        {
            new GenerateOsHashesCommand().SendUpdatesToClient.Should().BeTrue();
        }
    }
}
