using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacyImportFileLocatorDbFixture : DbTest<LegacyImportFileLocator, ImportFile>
    {
        [Test]
        public void should_save_the_real_path()
        {
            var folder = @"C:\media\Studio\Scene".AsOsAgnostic();
            var stale = Path.Combine(folder, "passion-hd.mp4");
            var actual = Path.Combine(folder, "passionhd.mp4");

            var repository = Mocker.Resolve<ImportFileRepository>();
            Mocker.SetConstant<IImportFileRepository>(repository);

            var file = repository.Insert(new ImportFile
            {
                RootFolderId = 1,
                Path = stale,
                RelativePath = Path.Combine("Studio", "Scene", "passion-hd.mp4"),
                Name = "passion-hd",
                OsHash = "stale"
            });

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(folder)).Returns(true);
            Mocker.GetMock<IDiskScanService>().Setup(s => s.GetVideoFiles(folder, false)).Returns(new[] { actual });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.FindByFullPath(actual)).Returns((MovieFile)null);

            Subject.Locate(file);

            var saved = repository.Get(file.Id);

            saved.Path.Should().Be(actual);
            saved.Name.Should().Be("passionhd");
            saved.OsHash.Should().BeNull();
        }
    }
}
