using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class ImportFileRepositoryFixture : DbTest<ImportFileRepository, ImportFile>
    {
        [Test]
        public void should_get_only_files_without_oshash_up_to_limit()
        {
            var items = Enumerable.Range(1, 5)
                .Select(i => new ImportFile
                {
                    RootFolderId = 1,
                    Path = $"/import/file{i}.mp4",
                    RelativePath = $"file{i}.mp4",
                    Name = $"file{i}",
                    OsHash = i <= 2 ? "0123456789abcdef" : null
                })
                .ToList();

            Db.InsertMany(items);

            var all = Subject.GetFilesWithoutOsHash(0, 100);

            all.Should().HaveCount(3);
            all.Should().OnlyContain(f => f.OsHash == null);
            Subject.GetFilesWithoutOsHash(0, 2).Should().HaveCount(2);

            var afterFirst = Subject.GetFilesWithoutOsHash(all[0].Id, 100);

            afterFirst.Should().HaveCount(2);
            afterFirst.Should().OnlyContain(f => f.Id > all[0].Id);
        }

        private ImportFile GivenStoredFile()
        {
            var file = new ImportFile
            {
                RootFolderId = 1,
                Path = "/import/file.mp4",
                RelativePath = "file.mp4",
                Name = "file"
            };

            Db.Insert(file);

            return file;
        }

        [Test]
        public void should_round_trip_phash_through_save_and_leave_file_out_of_the_phash_queue()
        {
            var file = GivenStoredFile();
            var attemptedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            file.PHash = "abcdef0123";
            file.PHashAttemptedAt = attemptedAt;

            Subject.SavePHashes(new List<ImportFile> { file });

            var stored = Subject.Get(file.Id);
            stored.PHash.Should().Be("abcdef0123");
            stored.PHashAttemptedAt.Should().Be(attemptedAt);
            Subject.GetFilesNeedingPHash(100).Should().NotContain(f => f.Id == file.Id);
        }

        [Test]
        public void phash_save_should_not_touch_oshash_columns()
        {
            var file = GivenStoredFile();
            var hashedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            // A concurrent oshash run stores its columns after this copy of the row was loaded
            var other = Subject.Get(file.Id);
            other.OsHash = "0123456789abcdef";
            other.HashedFileModifiedAt = hashedAt;
            Subject.SaveOsHashes(new List<ImportFile> { other });

            file.PHash = "ff";
            file.PHashAttemptedAt = DateTime.UtcNow;
            Subject.SavePHashes(new List<ImportFile> { file });

            var stored = Subject.Get(file.Id);
            stored.OsHash.Should().Be("0123456789abcdef");
            stored.HashedFileModifiedAt.Should().Be(hashedAt);
            stored.PHash.Should().Be("ff");
        }

        [Test]
        public void oshash_save_should_not_touch_phash_columns()
        {
            var file = GivenStoredFile();
            var attemptedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

            var other = Subject.Get(file.Id);
            other.PHash = "ff";
            other.PHashAttemptedAt = attemptedAt;
            Subject.SavePHashes(new List<ImportFile> { other });

            file.OsHash = "0123456789abcdef";
            file.HashedFileModifiedAt = DateTime.UtcNow;
            Subject.SaveOsHashes(new List<ImportFile> { file });

            var stored = Subject.Get(file.Id);
            stored.PHash.Should().Be("ff");
            stored.PHashAttemptedAt.Should().Be(attemptedAt);
            stored.OsHash.Should().Be("0123456789abcdef");
        }

        [Test]
        public void should_get_only_files_needing_phash_up_to_limit()
        {
            var items = Enumerable.Range(1, 5)
                .Select(i => new ImportFile
                {
                    RootFolderId = 1,
                    Path = $"/import/file{i}.mp4",
                    RelativePath = $"file{i}.mp4",
                    Name = $"file{i}",
                    PHash = i == 1 ? "abcdef" : null,
                    PHashAttemptedAt = i == 2 ? DateTime.UtcNow : null
                })
                .ToList();

            Db.InsertMany(items);

            var all = Subject.GetFilesNeedingPHash(100);

            all.Should().HaveCount(3);
            all.Should().OnlyContain(f => f.PHash == null && f.PHashAttemptedAt == null);
            all.Select(f => f.Id).Should().BeInAscendingOrder();
            Subject.GetFilesNeedingPHash(2).Should().HaveCount(2);
        }
    }
}
