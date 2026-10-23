using System;
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
