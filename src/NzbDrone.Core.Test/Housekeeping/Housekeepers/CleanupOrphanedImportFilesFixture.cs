using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupOrphanedImportFilesFixture : DbTest<CleanupOrphanedImportFiles, ImportFile>
    {
        [Test]
        public void should_delete_import_files_of_a_removed_root_folder()
        {
            var rootFolder = Builder<RootFolder>.CreateNew().BuildNew();
            Db.Insert(rootFolder);

            var kept = Builder<ImportFile>.CreateNew().With(f => f.RootFolderId = rootFolder.Id).BuildNew();
            var orphan = Builder<ImportFile>.CreateNew().With(f => f.RootFolderId = rootFolder.Id + 1).BuildNew();
            Db.Insert(kept);
            Db.Insert(orphan);

            Subject.Clean();

            AllStoredModels.Should().ContainSingle().Which.Id.Should().Be(kept.Id);
        }
    }
}
