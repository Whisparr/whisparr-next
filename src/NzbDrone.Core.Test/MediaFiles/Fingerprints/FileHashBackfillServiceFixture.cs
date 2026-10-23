using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
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
        private List<List<MovieFile>> _movieFileSaves;
        private List<List<ImportFile>> _importFileSaves;
        private List<List<MovieFile>> _pHashMovieFileSaves;
        private List<List<ImportFile>> _pHashImportFileSaves;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 5, Path = Path.Combine(Path.GetTempPath(), "library", "movie") };
            _movieFileSaves = new List<List<MovieFile>>();
            _importFileSaves = new List<List<ImportFile>>();
            _pHashMovieFileSaves = new List<List<MovieFile>>();
            _pHashImportFileSaves = new List<List<ImportFile>>();

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Movie> { _movie });

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesWithoutOsHash(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new List<MovieFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.SaveOsHashes(It.IsAny<List<MovieFile>>()))
                .Callback<List<MovieFile>>(l => _movieFileSaves.Add(l.ToList()));

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.SavePHashes(It.IsAny<List<MovieFile>>()))
                .Callback<List<MovieFile>>(l => _pHashMovieFileSaves.Add(l.ToList()));

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.GetFilesWithoutOsHash(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new List<ImportFile>());

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.SaveOsHashes(It.IsAny<List<ImportFile>>()))
                .Callback<List<ImportFile>>(l => _importFileSaves.Add(l.ToList()));

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.SavePHashes(It.IsAny<List<ImportFile>>()))
                .Callback<List<ImportFile>>(l => _pHashImportFileSaves.Add(l.ToList()));

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new List<MovieFile>());

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()))
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

        private void GivenMovieFiles(int afterId, int limit, List<MovieFile> files)
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithoutOsHash(afterId, limit)).Returns(files);
        }

        private void GivenImportFiles(int afterId, int limit, List<ImportFile> files)
        {
            Mocker.GetMock<IImportlFileService>().Setup(s => s.GetFilesWithoutOsHash(afterId, limit)).Returns(files);
        }

        [Test]
        public void should_hash_mapped_file_and_save_hash_with_modified_stamp()
        {
            var files = MovieFiles(1);
            GivenMovieFiles(0, 2000, files);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().Be("hash-file1.mp4");
            files[0].HashedFileModifiedAt.Should().Be(LastWrite);
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(Path.Combine(_movie.Path, "file1.mp4")), Times.Once());
            _movieFileSaves.Should().ContainSingle().Which.Should().Equal(files);
        }

        [Test]
        public void should_hash_unmapped_file_by_original_file_path()
        {
            var original = Path.Combine(Path.GetTempPath(), "downloads", "orphan.mp4");
            var files = new List<MovieFile> { new() { Id = 1, MovieId = 0, OriginalFilePath = original } };
            GivenMovieFiles(0, 2000, files);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().Be("hash-orphan.mp4");
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(original), Times.Once());
        }

        [Test]
        public void should_hash_import_files_by_path()
        {
            var files = ImportFiles(2);
            GivenImportFiles(0, 2000, files);

            Subject.Execute(new GenerateOsHashesCommand());

            files.Select(f => f.OsHash).Should().Equal("hash-import1.mp4", "hash-import2.mp4");
            files.Should().OnlyContain(f => f.HashedFileModifiedAt == LastWrite);
            _importFileSaves.Should().ContainSingle().Which.Should().Equal(files);
        }

        [Test]
        public void should_hash_at_most_2000_files_per_run_with_movie_files_first()
        {
            GivenMovieFiles(0, 2000, MovieFiles(1500));
            GivenImportFiles(0, 500, ImportFiles(500));

            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(It.IsAny<string>()), Times.Exactly(2000));
            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesWithoutOsHash(0, 500), Times.Once());
        }

        [Test]
        public void should_not_query_import_files_when_movie_files_use_the_whole_budget()
        {
            GivenMovieFiles(0, 2000, MovieFiles(2000));

            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesWithoutOsHash(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(It.IsAny<string>()), Times.Exactly(2000));
        }

        [Test]
        public void should_skip_file_missing_on_disk_without_hashing()
        {
            var files = MovieFiles(2);
            GivenMovieFiles(0, 2000, files);
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(Path.Combine(_movie.Path, "file1.mp4")))
                .Returns(false);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().BeNull();
            files[1].OsHash.Should().Be("hash-file2.mp4");
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(Path.Combine(_movie.Path, "file1.mp4")), Times.Never());
            _movieFileSaves.Should().ContainSingle().Which.Select(f => f.Id).Should().Equal(2);
        }

        [Test]
        public void should_skip_mapped_file_whose_movie_is_missing()
        {
            var files = MovieFiles(1, movieId: 99);
            GivenMovieFiles(0, 2000, files);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().BeNull();
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_leave_hash_null_on_failure_and_continue()
        {
            var files = MovieFiles(2);
            GivenMovieFiles(0, 2000, files);
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputeOsHash(Path.Combine(_movie.Path, "file1.mp4")))
                .Returns((string)null);

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().BeNull();
            files[0].HashedFileModifiedAt.Should().BeNull();
            files[1].OsHash.Should().Be("hash-file2.mp4");
            _movieFileSaves.Should().ContainSingle().Which.Select(f => f.Id).Should().Equal(2);
        }

        [Test]
        public void should_not_save_a_hash_without_a_last_write_stamp()
        {
            var files = MovieFiles(1);
            GivenMovieFiles(0, 2000, files);
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileGetLastWrite(It.IsAny<string>()))
                .Throws(new IOException());

            Subject.Execute(new GenerateOsHashesCommand());

            files[0].OsHash.Should().BeNull();
            _movieFileSaves.Should().BeEmpty();
        }

        [Test]
        public void should_not_save_when_nothing_was_hashed()
        {
            GivenMovieFiles(0, 2000, MovieFiles(1));
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns((string)null);

            Subject.Execute(new GenerateOsHashesCommand());

            _movieFileSaves.Should().BeEmpty();
            _importFileSaves.Should().BeEmpty();
        }

        [Test]
        public void should_load_movies_in_a_single_call()
        {
            GivenMovieFiles(0, 2000, MovieFiles(3));

            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IMovieService>().Verify(s => s.GetMovies(It.IsAny<IEnumerable<int>>()), Times.Once());
        }

        [Test]
        public void should_flush_every_100_files_and_at_the_end()
        {
            GivenMovieFiles(0, 2000, MovieFiles(250));

            Subject.Execute(new GenerateOsHashesCommand());

            _movieFileSaves.Select(s => s.Count).Should().Equal(100, 100, 50);
        }

        [Test]
        public void should_persist_earlier_batches_when_a_later_file_throws()
        {
            GivenMovieFiles(0, 2000, MovieFiles(150));
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(Path.Combine(_movie.Path, "file120.mp4")))
                .Throws(new InvalidOperationException("boom"));

            var act = () => Subject.Execute(new GenerateOsHashesCommand());

            act.Should().Throw<InvalidOperationException>();
            _movieFileSaves.Should().ContainSingle().Which.Should().HaveCount(100);
        }

        [Test]
        public void should_not_reselect_failed_rows_until_the_pass_wraps()
        {
            // First run asks for a full page, so the watermark moves to the last row seen
            var firstPage = MovieFiles(2000);
            GivenMovieFiles(0, 2000, firstPage);
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns((string)null);

            Subject.Execute(new GenerateOsHashesCommand());
            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesWithoutOsHash(0, 2000), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesWithoutOsHash(2000, 2000), Times.Once());
        }

        [Test]
        public void should_hash_later_rows_even_when_2000_earlier_rows_always_fail()
        {
            GivenMovieFiles(0, 2000, MovieFiles(2000));
            var later = MovieFiles(3, firstId: 2001);
            GivenMovieFiles(2000, 2000, later);
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.Is<string>(p => Path.GetFileName(p).StartsWith("file") && int.Parse(Path.GetFileNameWithoutExtension(p).Substring(4)) <= 2000)))
                .Returns(false);

            Subject.Execute(new GenerateOsHashesCommand());
            Subject.Execute(new GenerateOsHashesCommand());

            later.Should().OnlyContain(f => f.OsHash != null);
        }

        [Test]
        public void should_reset_the_watermark_when_a_query_returns_fewer_rows_than_asked()
        {
            GivenMovieFiles(0, 2000, MovieFiles(2000));
            GivenMovieFiles(2000, 2000, MovieFiles(3, firstId: 2001));

            Subject.Execute(new GenerateOsHashesCommand());
            Subject.Execute(new GenerateOsHashesCommand());
            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesWithoutOsHash(0, 2000), Times.Exactly(2));
            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesWithoutOsHash(2000, 2000), Times.Once());
        }

        [Test]
        public void should_track_import_file_watermark_separately_and_wrap()
        {
            GivenImportFiles(0, 2000, ImportFiles(2000));

            Subject.Execute(new GenerateOsHashesCommand());
            Subject.Execute(new GenerateOsHashesCommand());
            Subject.Execute(new GenerateOsHashesCommand());

            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesWithoutOsHash(0, 2000), Times.Exactly(2));
            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesWithoutOsHash(2000, 2000), Times.Once());
        }

        [Test]
        public void command_should_send_updates_to_client()
        {
            new GenerateOsHashesCommand().SendUpdatesToClient.Should().BeTrue();
        }

        private void GivenPHashEnabled(bool enabled = true, bool canCompute = true)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.EnablePerceptualHashing).Returns(enabled);
            Mocker.GetMock<IVideoHashService>().SetupGet(s => s.CanComputePHash).Returns(canCompute);
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputePHash(It.IsAny<string>()))
                .Returns<string>(p => "ph-" + Path.GetFileName(p));
        }

        // A chunk shorter than the chunk size ends the pass, so one short chunk is the whole table
        private void GivenPHashMovieFiles(List<MovieFile> files)
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(0, FileHashBackfillService.PHashChunkSize))
                .Returns(files);
        }

        private void GivenPHashImportFiles(List<ImportFile> files)
        {
            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.GetFilesNeedingPHash(0, FileHashBackfillService.PHashChunkSize))
                .Returns(files);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void phash_should_not_touch_repositories_when_disabled_or_unavailable(bool enabled, bool canCompute)
        {
            GivenPHashEnabled(enabled, canCompute);

            Subject.Execute(new GenerateVideoHashesCommand());

            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.SavePHashes(It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.SavePHashes(It.IsAny<List<ImportFile>>()), Times.Never());
        }

        [Test]
        public void phash_should_store_hash_and_stamp_and_save_each_file_on_its_own()
        {
            GivenPHashEnabled();
            var files = MovieFiles(2);
            GivenPHashMovieFiles(files);
            var now = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            Subject.UtcNow = () => now;

            Subject.Execute(new GenerateVideoHashesCommand());

            files.Select(f => f.PHash).Should().Equal("ph-file1.mp4", "ph-file2.mp4");
            files.Should().OnlyContain(f => f.PHashAttemptedAt == now);
            _pHashMovieFileSaves.Should().HaveCount(2);
            _pHashMovieFileSaves.Should().OnlyContain(s => s.Count == 1);
            _movieFileSaves.Should().BeEmpty();
        }

        [Test]
        public void phash_should_stamp_attempt_when_hashing_fails()
        {
            GivenPHashEnabled();
            var files = MovieFiles(2);
            GivenPHashMovieFiles(files);
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputePHash(Path.Combine(_movie.Path, "file1.mp4")))
                .Returns((string)null);

            Subject.Execute(new GenerateVideoHashesCommand());

            files[0].PHash.Should().BeNull();
            files[0].PHashAttemptedAt.Should().NotBeNull();
            files[1].PHash.Should().Be("ph-file2.mp4");
            _pHashMovieFileSaves.Should().HaveCount(2);
        }

        [Test]
        public void phash_should_skip_file_missing_on_disk_without_stamping_it()
        {
            GivenPHashEnabled();
            var files = MovieFiles(1);
            GivenPHashMovieFiles(files);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(false);

            Subject.Execute(new GenerateVideoHashesCommand());

            files[0].PHash.Should().BeNull();
            files[0].PHashAttemptedAt.Should().BeNull();
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(It.IsAny<string>()), Times.Never());
            _pHashMovieFileSaves.Should().BeEmpty();
        }

        [Test]
        public void phash_should_not_fetch_skipped_missing_files_again_within_a_pass()
        {
            GivenPHashEnabled();
            var chunkSize = FileHashBackfillService.PHashChunkSize;
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(false);
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(0, chunkSize))
                .Returns(MovieFiles(chunkSize));
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(chunkSize, chunkSize))
                .Returns(new List<MovieFile>());

            Subject.Execute(new GenerateVideoHashesCommand());

            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesNeedingPHash(0, chunkSize), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesNeedingPHash(chunkSize, chunkSize), Times.Once());
            _pHashMovieFileSaves.Should().BeEmpty();
        }

        [Test]
        public void phash_watermark_should_wrap_to_the_start_once_a_pass_reaches_the_end()
        {
            GivenPHashEnabled();
            var chunkSize = FileHashBackfillService.PHashChunkSize;
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(false);
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(0, chunkSize))
                .Returns(MovieFiles(chunkSize));
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(chunkSize, chunkSize))
                .Returns(new List<MovieFile>());

            Subject.Execute(new GenerateVideoHashesCommand());
            Subject.Execute(new GenerateVideoHashesCommand());

            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesNeedingPHash(0, chunkSize), Times.Exactly(2));
        }

        [Test]
        public void phash_should_stop_without_stamping_when_the_first_five_files_all_fail()
        {
            GivenPHashEnabled();
            var files = MovieFiles(8);
            GivenPHashMovieFiles(files);
            GivenPHashImportFiles(ImportFiles(2));
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputePHash(It.IsAny<string>())).Returns((string)null);

            Subject.Execute(new GenerateVideoHashesCommand());

            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(It.IsAny<string>()), Times.Exactly(FileHashBackfillService.HeldFailureLimit));
            files.Should().OnlyContain(f => f.PHashAttemptedAt == null && f.PHash == null);
            _pHashMovieFileSaves.Should().BeEmpty();
            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void phash_should_stamp_held_failures_once_a_file_succeeds()
        {
            GivenPHashEnabled();
            var files = MovieFiles(3);
            GivenPHashMovieFiles(files);
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputePHash(It.IsAny<string>()))
                .Returns<string>(p => Path.GetFileName(p) == "file3.mp4" ? "ph-file3.mp4" : null);

            Subject.Execute(new GenerateVideoHashesCommand());

            files.Select(f => f.PHash).Should().Equal(null, null, "ph-file3.mp4");
            files.Should().OnlyContain(f => f.PHashAttemptedAt != null);
            _pHashMovieFileSaves.Should().HaveCount(3);
        }

        [Test]
        public void phash_should_stamp_later_failures_immediately_after_a_success()
        {
            GivenPHashEnabled();
            var files = MovieFiles(7);
            GivenPHashMovieFiles(files);
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputePHash(It.IsAny<string>()))
                .Returns<string>(p => Path.GetFileName(p) == "file1.mp4" ? "ph-file1.mp4" : null);

            Subject.Execute(new GenerateVideoHashesCommand());

            files[0].PHash.Should().Be("ph-file1.mp4");
            files.Should().OnlyContain(f => f.PHashAttemptedAt != null);
            _pHashMovieFileSaves.Should().HaveCount(7);
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(It.IsAny<string>()), Times.Exactly(7));
        }

        [Test]
        public void phash_should_hash_unmapped_movie_file_by_original_file_path()
        {
            GivenPHashEnabled();
            var original = Path.Combine(Path.GetTempPath(), "downloads", "orphan.mp4");
            var files = new List<MovieFile> { new() { Id = 1, MovieId = 0, OriginalFilePath = original } };
            GivenPHashMovieFiles(files);

            Subject.Execute(new GenerateVideoHashesCommand());

            files[0].PHash.Should().Be("ph-orphan.mp4");
            files[0].PHashAttemptedAt.Should().NotBeNull();
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(original), Times.Once());
        }

        [Test]
        public void phash_should_skip_mapped_file_whose_movie_is_missing_without_stamping()
        {
            GivenPHashEnabled();
            var files = MovieFiles(1, movieId: 99);
            GivenPHashMovieFiles(files);

            Subject.Execute(new GenerateVideoHashesCommand());

            files[0].PHashAttemptedAt.Should().BeNull();
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void phash_should_process_import_files_after_movie_files()
        {
            GivenPHashEnabled();
            var movieFiles = MovieFiles(1);
            var importFiles = ImportFiles(2);
            GivenPHashMovieFiles(movieFiles);
            GivenPHashImportFiles(importFiles);
            var order = new List<string>();
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputePHash(It.IsAny<string>()))
                .Callback<string>(p => order.Add(Path.GetFileName(p)))
                .Returns<string>(p => "ph-" + Path.GetFileName(p));

            Subject.Execute(new GenerateVideoHashesCommand());

            order.Should().Equal("file1.mp4", "import1.mp4", "import2.mp4");
            importFiles.Select(f => f.PHash).Should().Equal("ph-import1.mp4", "ph-import2.mp4");
            importFiles.Should().OnlyContain(f => f.PHashAttemptedAt != null);
            _pHashImportFileSaves.Should().HaveCount(2);
            _importFileSaves.Should().BeEmpty();
        }

        [Test]
        public void phash_should_keep_fetching_chunks_until_no_work_is_left()
        {
            GivenPHashEnabled();
            var chunkSize = FileHashBackfillService.PHashChunkSize;
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(0, chunkSize))
                .Returns(MovieFiles(chunkSize));
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(chunkSize, chunkSize))
                .Returns(MovieFiles(1, firstId: chunkSize + 1));

            Subject.Execute(new GenerateVideoHashesCommand());

            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(It.IsAny<string>()), Times.Exactly(chunkSize + 1));
            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesNeedingPHash(0, chunkSize), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesNeedingPHash(chunkSize, chunkSize), Times.Once());
        }

        [Test]
        public void phash_should_stop_starting_files_after_15_minutes_of_wall_time()
        {
            GivenPHashEnabled();
            var files = MovieFiles(5);
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(files);
            var now = new DateTime(2026, 5, 6, 7, 0, 0, DateTimeKind.Utc);
            Subject.UtcNow = () => now;
            Mocker.GetMock<IVideoHashService>()
                .Setup(s => s.ComputePHash(It.IsAny<string>()))
                .Callback<string>(_ => now = now.AddMinutes(6))
                .Returns("ph");

            Subject.Execute(new GenerateVideoHashesCommand());

            // Files start at minutes 0, 6 and 12; the one due at minute 18 is past the budget
            files.Count(f => f.PHashAttemptedAt != null).Should().Be(3);
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(It.IsAny<string>()), Times.Exactly(3));
            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void phash_should_stop_cleanly_when_the_setting_is_turned_off_mid_run()
        {
            GivenPHashEnabled();
            var files = MovieFiles(3);
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(files);

            // On for the up-front check and the first file, off from the second file's check on
            Mocker.GetMock<IConfigService>()
                .SetupSequence(s => s.EnablePerceptualHashing)
                .Returns(true)
                .Returns(true)
                .Returns(false);

            Subject.Execute(new GenerateVideoHashesCommand());

            files.Count(f => f.PHashAttemptedAt != null).Should().Be(1);
            files[0].PHash.Should().Be("ph-file1.mp4");
            _pHashMovieFileSaves.Should().ContainSingle();
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputePHash(It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesNeedingPHash(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void phash_command_should_send_updates_to_client()
        {
            new GenerateVideoHashesCommand().SendUpdatesToClient.Should().BeTrue();
        }
    }
}
