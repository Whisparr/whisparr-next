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

            Subject.GetFilesWithoutOsHash(100).Should().HaveCount(3);
            Subject.GetFilesWithoutOsHash(100).Should().OnlyContain(f => f.OsHash == null);
            Subject.GetFilesWithoutOsHash(2).Should().HaveCount(2);
        }
    }
}
