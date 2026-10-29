using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.LostFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.DiskScanServiceTests
{
    [TestFixture]
    public class ScanFixture : CoreTest<DiskScanService>
    {
        private Movie _movie;
        private string _rootFolder;
        private string _otherMovieFolder;

        [SetUp]
        public void Setup()
        {
            _rootFolder = @"C:\Test\Movies".AsOsAgnostic();
            _otherMovieFolder = @"C:\Test\Movies\OtherMovie".AsOsAgnostic();
            var movieFolder = @"C:\Test\Movies\Movie".AsOsAgnostic();

            _movie = Builder<Movie>.CreateNew()
                .With(s => s.Path = movieFolder)
                                     .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(It.IsAny<string>()))
                  .Returns((string path) => Directory.GetParent(path).FullName);

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), null))
                  .Returns(_rootFolder);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MovieFile>());
        }

        private void GivenRootFolder(params string[] subfolders)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(_rootFolder))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetDirectories(_rootFolder))
                  .Returns(subfolders);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderEmpty(_rootFolder))
                  .Returns(subfolders.Empty());

            foreach (var folder in subfolders)
            {
                Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(folder))
                  .Returns(true);
            }
        }

        private void GivenMovieFolder()
        {
            GivenRootFolder(_movie.Path);
        }

        private void GivenFiles(IEnumerable<string> files)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(It.IsAny<string>(), true))
                  .Returns(files.ToArray());
        }

        [Test]
        public void should_not_scan_if_movie_root_folder_does_not_exist()
        {
            Subject.Scan(_movie);

            ExceptionVerification.ExpectedWarns(1);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.GetFiles(_movie.Path, true), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.CreateFolder(_movie.Path), Times.Never());

            Mocker.GetMock<IMediaFileTableCleanupService>()
                .Verify(v => v.Clean(It.IsAny<Movie>(), It.IsAny<List<string>>()), Times.Never());
        }

        [Test]
        public void should_not_sweep_empty_subfolders_when_folder_holds_other_items()
        {
            GivenMovieFolder();

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DeleteEmptyFolders)
                  .Returns(true);

            // A sibling scene shares the folder, and a movie's (empty) folder is nested in it
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.HoldsOtherItems(_movie.Id, _movie.Path))
                  .Returns(true);

            Subject.Scan(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.RemoveEmptySubfolders(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_sweep_empty_subfolders_when_folder_holds_no_other_items()
        {
            GivenMovieFolder();

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DeleteEmptyFolders)
                  .Returns(true);

            Subject.Scan(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.RemoveEmptySubfolders(_movie.Path), Times.Once());
        }

        [Test]
        public void should_not_scan_if_movie_root_folder_is_empty()
        {
            GivenRootFolder();

            Subject.Scan(_movie);

            ExceptionVerification.ExpectedWarns(1);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.GetFiles(_movie.Path, true), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.CreateFolder(_movie.Path), Times.Never());

            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Verify(v => v.Clean(It.IsAny<Movie>(), It.IsAny<List<string>>()), Times.Never());

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), _movie, false), Times.Never());
        }

        [Test]
        public void should_create_if_movie_folder_does_not_exist_but_create_folder_enabled()
        {
            GivenRootFolder(_otherMovieFolder);

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.CreateEmptyMovieFolders)
                  .Returns(true);

            Subject.Scan(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.CreateFolder(_movie.Path), Times.Once());
        }

        private static readonly DateTime HashedAt = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        private MovieFile GivenExistingFileOnDisk(long storedSize, long diskSize, string osHash, DateTime? hashedAt, DateTime diskLastWrite)
        {
            GivenMovieFolder();

            var relativePath = "movie.mkv";
            var path = Path.Combine(_movie.Path, relativePath);
            var file = new MovieFile
            {
                Id = 11,
                MovieId = _movie.Id,
                RelativePath = relativePath,
                Size = storedSize,
                OsHash = osHash,
                PHash = osHash == null ? null : "abcdef",
                PHashAttemptedAt = osHash == null ? null : HashedAt,
                HashedFileModifiedAt = hashedAt
            };

            GivenFiles(new List<string> { path });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MovieFile> { file });

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(path)).Returns(diskSize);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileGetLastWrite(path)).Returns(diskLastWrite);
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(path)).Returns("fedcba9876543210");

            return file;
        }

        private void VerifyPersisted(MovieFile file, Times times)
        {
            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Update(It.Is<List<MovieFile>>(l => l.Contains(file))), times);
        }

        [Test]
        public void should_clear_phash_and_rehash_when_size_changed()
        {
            var file = GivenExistingFileOnDisk(100, 200, "0123456789abcdef", HashedAt, HashedAt);

            Subject.Scan(_movie);

            file.OsHash.Should().Be("fedcba9876543210");
            file.HashedFileModifiedAt.Should().Be(HashedAt);
            file.PHash.Should().BeNull();
            file.PHashAttemptedAt.Should().BeNull();
            file.Size.Should().Be(200);
            VerifyPersisted(file, Times.Once());
        }

        [Test]
        public void should_clear_phash_and_rehash_when_last_write_changed()
        {
            var changed = HashedAt.AddMinutes(5);
            var file = GivenExistingFileOnDisk(100, 100, "0123456789abcdef", HashedAt, changed);

            Subject.Scan(_movie);

            file.OsHash.Should().Be("fedcba9876543210");
            file.HashedFileModifiedAt.Should().Be(changed);
            file.PHash.Should().BeNull();
            file.PHashAttemptedAt.Should().BeNull();
            VerifyPersisted(file, Times.Once());
        }

        [Test]
        public void should_retry_oshash_without_touching_phash_when_hash_is_missing()
        {
            var file = GivenExistingFileOnDisk(100, 100, null, null, HashedAt);
            file.PHash = "abcdef";
            file.PHashAttemptedAt = HashedAt;

            Subject.Scan(_movie);

            file.OsHash.Should().Be("fedcba9876543210");
            file.HashedFileModifiedAt.Should().Be(HashedAt);
            file.PHash.Should().Be("abcdef");
            file.PHashAttemptedAt.Should().Be(HashedAt);
            VerifyPersisted(file, Times.Once());
        }

        [Test]
        public void should_not_hash_unchanged_file_that_already_has_oshash()
        {
            var file = GivenExistingFileOnDisk(100, 100, "0123456789abcdef", HashedAt, HashedAt);

            Subject.Scan(_movie);

            file.OsHash.Should().Be("0123456789abcdef");
            file.PHash.Should().Be("abcdef");
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(It.IsAny<string>()), Times.Never());
            VerifyPersisted(file, Times.Never());
        }

        [Test]
        public void should_treat_sub_second_last_write_difference_as_unchanged()
        {
            var file = GivenExistingFileOnDisk(100, 100, "0123456789abcdef", HashedAt, HashedAt.AddMilliseconds(789));

            Subject.Scan(_movie);

            file.PHash.Should().Be("abcdef");
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(It.IsAny<string>()), Times.Never());
            VerifyPersisted(file, Times.Never());
        }

        [Test]
        public void should_record_last_write_when_hash_exists_without_one()
        {
            var file = GivenExistingFileOnDisk(100, 100, "0123456789abcdef", null, HashedAt);

            Subject.Scan(_movie);

            file.OsHash.Should().Be("0123456789abcdef");
            file.PHash.Should().Be("abcdef");
            file.HashedFileModifiedAt.Should().Be(HashedAt);
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(It.IsAny<string>()), Times.Never());
            VerifyPersisted(file, Times.Once());
        }

        [Test]
        public void should_not_fail_scan_when_file_vanished_before_last_write_was_read()
        {
            var file = GivenExistingFileOnDisk(100, 100, "0123456789abcdef", HashedAt, HashedAt);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileGetLastWrite(It.IsAny<string>()))
                  .Throws(new FileNotFoundException());

            Subject.Scan(_movie);

            file.OsHash.Should().Be("0123456789abcdef");
            file.HashedFileModifiedAt.Should().Be(HashedAt);
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(It.IsAny<string>()), Times.Never());
            VerifyPersisted(file, Times.Never());
        }

        [Test]
        public void should_keep_phash_and_restamp_when_only_mtime_changed_and_oshash_matches()
        {
            var changed = HashedAt.AddMinutes(5);
            var file = GivenExistingFileOnDisk(100, 100, "fedcba9876543210", HashedAt, changed);

            Subject.Scan(_movie);

            file.OsHash.Should().Be("fedcba9876543210");
            file.PHash.Should().Be("abcdef");
            file.PHashAttemptedAt.Should().Be(HashedAt);
            file.HashedFileModifiedAt.Should().Be(changed);
            VerifyPersisted(file, Times.Once());
        }

        [Test]
        public void should_clear_phash_when_only_mtime_changed_and_oshash_differs()
        {
            var changed = HashedAt.AddMinutes(5);
            var file = GivenExistingFileOnDisk(100, 100, "0123456789abcdef", HashedAt, changed);

            Subject.Scan(_movie);

            file.OsHash.Should().Be("fedcba9876543210");
            file.PHash.Should().BeNull();
            file.PHashAttemptedAt.Should().BeNull();
            file.HashedFileModifiedAt.Should().Be(changed);
            VerifyPersisted(file, Times.Once());
        }

        [Test]
        public void should_keep_every_hash_when_only_mtime_changed_and_recompute_fails()
        {
            var changed = HashedAt.AddMinutes(5);
            var file = GivenExistingFileOnDisk(100, 100, "0123456789abcdef", HashedAt, changed);

            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns((string)null);

            Subject.Scan(_movie);

            file.OsHash.Should().Be("0123456789abcdef");
            file.PHash.Should().Be("abcdef");
            file.PHashAttemptedAt.Should().Be(HashedAt);
            file.HashedFileModifiedAt.Should().Be(HashedAt);
            VerifyPersisted(file, Times.Never());
        }

        [Test]
        public void should_persist_hash_changed_file_once_on_manual_scan_when_media_info_fails()
        {
            var file = GivenExistingFileOnDisk(100, 100, null, null, HashedAt);

            Subject.Scan(_movie, true);

            file.OsHash.Should().Be("fedcba9876543210");
            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Update(It.Is<List<MovieFile>>(l => l.Count(f => f == file) == 1)), Times.Once());
        }

        [Test]
        public void should_not_persist_again_on_manual_scan_when_media_info_update_succeeds()
        {
            var file = GivenExistingFileOnDisk(100, 100, null, null, HashedAt);

            Mocker.GetMock<IUpdateMediaInfo>()
                  .Setup(s => s.Update(file, _movie))
                  .Returns(true);

            Subject.Scan(_movie, true);

            file.OsHash.Should().Be("fedcba9876543210");
            Mocker.GetMock<IUpdateMediaInfo>().Verify(v => v.Update(file, _movie), Times.Once());
            VerifyPersisted(file, Times.Never());
        }

        private MovieFile GivenMissingRow(int id, string relativePath, string osHash)
        {
            return new MovieFile
            {
                Id = id,
                MovieId = _movie.Id,
                RelativePath = relativePath,
                Size = 100,
                OsHash = osHash,
                PHash = "abcdef",
                PHashAttemptedAt = HashedAt,
                HashedFileModifiedAt = HashedAt,
                MatchTier = MatchTier.Hash
            };
        }

        private string GivenNewFileOnDisk(string relativePath, string osHash)
        {
            var path = Path.Combine(_movie.Path, relativePath);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(path)).Returns(100);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileGetLastWrite(path)).Returns(HashedAt.AddMinutes(10));
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(path)).Returns(osHash);

            return path;
        }

        private void GivenRowsAndDisk(List<MovieFile> rows, params string[] filesOnDisk)
        {
            GivenMovieFolder();
            GivenFiles(filesOnDisk);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(rows);
        }

        private void VerifyImportedOnly(params string[] paths)
        {
            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.SequenceEqual(paths)), _movie, false), Times.Once());
        }

        [Test]
        public void should_relink_missing_row_to_new_file_with_same_oshash()
        {
            var row = GivenMissingRow(21, "old.mkv", "aaaaaaaaaaaaaaaa");
            var newPath = GivenNewFileOnDisk("new.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { row }, newPath);

            Subject.Scan(_movie);

            row.RelativePath.Should().Be("new.mkv");
            row.HashedFileModifiedAt.Should().Be(HashedAt.AddMinutes(10));
            row.OsHash.Should().Be("aaaaaaaaaaaaaaaa");
            row.PHash.Should().Be("abcdef");
            row.PHashAttemptedAt.Should().Be(HashedAt);
            row.MatchTier.Should().Be(MatchTier.Hash);
            row.Size.Should().Be(100);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(row), Times.Once());
            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Verify(v => v.Clean(_movie, It.Is<List<string>>(l => l.Contains(newPath))), Times.Once());
            VerifyImportedOnly();
        }

        [Test]
        public void should_import_other_new_file_with_different_oshash_normally()
        {
            var row = GivenMissingRow(21, "old.mkv", "aaaaaaaaaaaaaaaa");
            var renamedPath = GivenNewFileOnDisk("new.mkv", "aaaaaaaaaaaaaaaa");
            var otherPath = GivenNewFileOnDisk("other.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile> { row }, renamedPath, otherPath);

            Subject.Scan(_movie);

            row.RelativePath.Should().Be("new.mkv");
            VerifyImportedOnly(otherPath);
        }

        [Test]
        public void should_not_hash_new_files_when_no_row_is_missing()
        {
            var existingPath = Path.Combine(_movie.Path, "movie.mkv");
            var row = GivenMissingRow(21, "movie.mkv", "aaaaaaaaaaaaaaaa");
            var newPath = GivenNewFileOnDisk("new.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile> { row }, existingPath, newPath);

            Subject.Scan(_movie);

            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(newPath), Times.Never());
            VerifyImportedOnly(newPath);
        }

        [Test]
        public void should_not_relink_when_two_missing_rows_share_the_oshash()
        {
            var first = GivenMissingRow(21, "old1.mkv", "aaaaaaaaaaaaaaaa");
            var second = GivenMissingRow(22, "old2.mkv", "aaaaaaaaaaaaaaaa");
            var newPath = GivenNewFileOnDisk("new.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { first, second }, newPath);

            Subject.Scan(_movie);

            first.RelativePath.Should().Be("old1.mkv");
            second.RelativePath.Should().Be("old2.mkv");
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<MovieFile>()), Times.Never());
            VerifyImportedOnly(newPath);
        }

        [Test]
        public void should_not_relink_when_two_new_files_share_the_oshash()
        {
            var row = GivenMissingRow(21, "old.mkv", "aaaaaaaaaaaaaaaa");
            var firstPath = GivenNewFileOnDisk("new1.mkv", "aaaaaaaaaaaaaaaa");
            var secondPath = GivenNewFileOnDisk("new2.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { row }, firstPath, secondPath);

            Subject.Scan(_movie);

            row.RelativePath.Should().Be("old.mkv");
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<MovieFile>()), Times.Never());
            VerifyImportedOnly(firstPath, secondPath);
        }

        [Test]
        public void should_not_relink_a_row_missing_from_the_listing_but_still_on_disk()
        {
            var row = GivenMissingRow(21, "old.mkv", "aaaaaaaaaaaaaaaa");
            var newPath = GivenNewFileOnDisk("new.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { row }, newPath);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(Path.Combine(_movie.Path, "old.mkv")))
                  .Returns(true);

            Subject.Scan(_movie);

            row.RelativePath.Should().Be("old.mkv");
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(newPath), Times.Never());
        }

        [Test]
        public void should_not_relink_missing_row_without_oshash()
        {
            var row = GivenMissingRow(21, "old.mkv", null);
            var newPath = GivenNewFileOnDisk("new.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { row }, newPath);

            Subject.Scan(_movie);

            row.RelativePath.Should().Be("old.mkv");
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(newPath), Times.Never());
            VerifyImportedOnly(newPath);
        }

        [Test]
        public void should_not_scan_extras_subfolder()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "EXTRAS", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Extras", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "EXTRAs", "file3.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "ExTrAs", "file4.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Exactly(2));

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_scan_various_extras_subfolders()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "Behind the Scenes", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Deleted Scenes", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Featurettes", "file3.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Interviews", "file4.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Sample", "file5.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Samples", "file6.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Scenes", "file7.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Shorts", "file8.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Trailers", "file9.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Other", "file9.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "The Count of Monte Cristo (2002) (1080p BluRay x265 10bit Tigole).mkv").AsOsAgnostic(),
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_scan_featurettes_subfolders()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "Featurettes", "An Epic Reborn.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Featurettes", "Deleted & Alternate Scenes.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Featurettes", "En Garde - Multi-Angle Dailies.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Featurettes", "Layer-By-Layer - Sound Design - Multiple Audio.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "The Count of Monte Cristo (2002) (1080p BluRay x265 10bit Tigole).mkv").AsOsAgnostic(),
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_create_if_movie_folder_does_not_exist_and_create_folder_disabled()
        {
            GivenRootFolder(_otherMovieFolder);

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.CreateEmptyMovieFolders)
                  .Returns(false);

            Subject.Scan(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.CreateFolder(_movie.Path), Times.Never());
        }

        [Test]
        public void should_clean_but_not_import_if_movie_folder_does_not_exist()
        {
            GivenRootFolder(_otherMovieFolder);

            Subject.Scan(_movie);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.FolderExists(_movie.Path), Times.Once());

            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Verify(v => v.Clean(It.IsAny<Movie>(), It.IsAny<List<string>>()), Times.Once());

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), _movie, false), Times.Never());
        }

        [Test]
        public void should_not_scan_AppleDouble_subfolder()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, ".AppleDouble", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, ".appledouble", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_scan_extras_movie_and_subfolders()
        {
            _movie.Path = @"C:\Test\Movies\Extras".AsOsAgnostic();

            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "Extras", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, ".AppleDouble", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e02.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 2", "s02e01.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 2", "s02e02.mkv").AsOsAgnostic(),
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 4), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_scan_subfolders_that_start_with_period()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, ".@__thumb", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, ".@__THUMB", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, ".hidden", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_scan_subfolder_of_season_folder_that_starts_with_a_period()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "Season 1", ".@__thumb", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", ".@__THUMB", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", ".hidden", "file2.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", ".AppleDouble", "s01e01.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_scan_Synology_eaDir()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "@eaDir", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_scan_thumb_folder()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, ".@__thumb", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_scan_dotHack_folder()
        {
            _movie.Path = @"C:\Test\TV\.hack".AsOsAgnostic();

            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "Season 1", "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Season 1", "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 2), _movie, false), Times.Once());
        }

        [Test]
        public void should_find_files_at_root_of_movie_folder()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "file1.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "s01e01.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 2), _movie, false), Times.Once());
        }

        [Test]
        public void should_exclude_inline_extra_files()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "Avatar (2009).mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "Deleted Scenes-deleted.mkv").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "The World of Pandora-other.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_exclude_osx_metadata_files()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_movie.Path, "._24 The Status Quo Combustion.mp4").AsOsAgnostic(),
                           Path.Combine(_movie.Path, "24 The Status Quo Combustion.mkv").AsOsAgnostic()
                       });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());
        }

        [Test]
        public void should_not_scan_excluded_files()
        {
            GivenMovieFolder();

            GivenFiles(new List<string>
            {
                Path.Combine(_movie.Path, ".DS_Store").AsOsAgnostic(),
                Path.Combine(_movie.Path, ".unmanic").AsOsAgnostic(),
                Path.Combine(_movie.Path, ".unmanic.part").AsOsAgnostic(),
                Path.Combine(_movie.Path, "24 The Status Quo Combustion.mkv").AsOsAgnostic()
            });

            Subject.Scan(_movie);

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<string>>(l => l.Count == 1), _movie, false), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<MovieScannedEvent>(c => c.Movie != null && c.PossibleExtraFiles.Count == 0)), Times.Once());
        }

        private void GivenSceneWithLostEntries()
        {
            _movie.MovieMetadata = new MovieMetadata { ItemType = ItemType.Scene };
            Mocker.GetMock<ILostMovieFileService>().Setup(s => s.Any()).Returns(true);
            Mocker.GetMock<ISharedFolderScanner>().Setup(s => s.TryBeginLostFileCheck(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_movie.Path, It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files);
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), _movie, false))
                  .Returns(new List<ImportDecision>());
        }

        [Test]
        public void missing_owned_file_is_recorded_when_no_in_folder_relink()
        {
            GivenSceneWithLostEntries();

            var row = GivenMissingRow(21, "old.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { row });

            var order = new List<string>();
            Mocker.GetMock<ILostMovieFileService>()
                  .Setup(s => s.Record(_movie, row))
                  .Callback(() => order.Add("record"));
            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Setup(s => s.Clean(_movie, It.IsAny<List<string>>()))
                  .Callback(() => order.Add("clean"));

            Subject.Scan(_movie);

            // Recorded before Clean deletes the row
            order.Should().Equal("record", "clean");
        }

        [Test]
        public void relinked_file_is_not_recorded_as_lost()
        {
            GivenSceneWithLostEntries();

            var row = GivenMissingRow(21, "old.mkv", "aaaaaaaaaaaaaaaa");
            var newPath = GivenNewFileOnDisk("new.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { row }, newPath);

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.Record(It.IsAny<Movie>(), It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void missing_file_of_a_movie_is_not_recorded()
        {
            var row = GivenMissingRow(21, "old.mkv", "aaaaaaaaaaaaaaaa");
            GivenRowsAndDisk(new List<MovieFile> { row });

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.Record(It.IsAny<Movie>(), It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void unowned_unidentified_file_matching_lost_entry_is_relinked_during_another_scenes_scan()
        {
            GivenSceneWithLostEntries();

            var path = GivenNewFileOnDisk("moved-in.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile>(), path);

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_movie.Path, It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files);
            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_movie, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.TryRelink(path, "bbbbbbbbbbbbbbbb", 100), Times.Once());
        }

        [Test]
        public void unknown_file_in_exclusive_folder_matching_lost_entry_is_relinked()
        {
            GivenSceneWithLostEntries();

            var path = GivenNewFileOnDisk("moved-in.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile>(), path);

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.TryRelink(path, "bbbbbbbbbbbbbbbb", 100), Times.Once());
        }

        [Test]
        public void rejected_unknown_file_in_exclusive_folder_matching_lost_entry_is_relinked()
        {
            GivenSceneWithLostEntries();

            var path = GivenNewFileOnDisk("moved-in.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile>(), path);

            // An exclusive folder decides every file: this one is rejected, so nothing imports it
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), _movie, false))
                  .Returns(new List<ImportDecision>
                  {
                      new(new Parser.Model.LocalMovie { Movie = _movie, Path = path }, new ImportRejection(ImportRejectionReason.Unknown, "not this scene"))
                  });

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.TryRelink(path, "bbbbbbbbbbbbbbbb", 100), Times.Once());
        }

        [Test]
        public void file_that_was_imported_is_not_checked_against_lost_entries()
        {
            GivenSceneWithLostEntries();

            var path = GivenNewFileOnDisk("claimed.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile>(), path);

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), _movie, false))
                  .Returns(new List<ImportDecision> { new(new Parser.Model.LocalMovie { Movie = _movie, Path = path }) });

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.TryRelink(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never());
        }

        [Test]
        public void file_tracked_by_another_item_is_not_a_relink_candidate_in_an_exclusive_folder()
        {
            GivenSceneWithLostEntries();

            var path = GivenNewFileOnDisk("tracked.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile>(), path);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_movie.Path, It.IsAny<List<string>>()))
                  .Returns(new List<string>());

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.TryRelink(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never());
        }

        [Test]
        public void file_checked_within_the_window_is_not_hashed_again()
        {
            GivenSceneWithLostEntries();
            Mocker.GetMock<ISharedFolderScanner>().Setup(s => s.TryBeginLostFileCheck(It.IsAny<string>())).Returns(false);

            var path = GivenNewFileOnDisk("moved-in.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile>(), path);

            Subject.Scan(_movie);

            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(path), Times.Never());
            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.TryRelink(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never());
        }

        [Test]
        public void does_not_hash_candidates_when_nothing_was_lost()
        {
            GivenSceneWithLostEntries();
            Mocker.GetMock<ILostMovieFileService>().Setup(s => s.Any()).Returns(false);

            var path = GivenNewFileOnDisk("moved-in.mkv", "bbbbbbbbbbbbbbbb");
            GivenRowsAndDisk(new List<MovieFile>(), path);

            Subject.Scan(_movie);

            Mocker.GetMock<ILostMovieFileService>().Verify(v => v.TryRelink(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never());
        }

        [Test]
        public void run_locked_runs_the_action_and_blocks_a_second_caller_on_the_same_folder()
        {
            var folder = @"C:\Test\Movies\LockedFolder".AsOsAgnostic();
            var other = new Movie { Id = _movie.Id + 1, Path = folder };
            var firstEntered = new System.Threading.ManualResetEventSlim();
            var release = new System.Threading.ManualResetEventSlim();
            var secondRan = false;

            var first = System.Threading.Tasks.Task.Run(() => Subject.RunLocked(_movie, folder, () =>
            {
                firstEntered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }));

            firstEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

            var second = System.Threading.Tasks.Task.Run(() => Subject.RunLocked(other, folder, () => secondRan = true));

            // Different scenes, same folder: the folder lock holds the second one back
            second.Wait(TimeSpan.FromMilliseconds(300)).Should().BeFalse();
            secondRan.Should().BeFalse();

            release.Set();
            System.Threading.Tasks.Task.WaitAll(new[] { first, second }, TimeSpan.FromSeconds(10)).Should().BeTrue();
            secondRan.Should().BeTrue();
        }
    }
}
