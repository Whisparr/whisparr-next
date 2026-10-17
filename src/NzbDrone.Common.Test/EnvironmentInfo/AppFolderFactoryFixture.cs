using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.EnvironmentInfo
{
    [TestFixture]
    public class AppFolderFactoryFixture : TestBase<AppFolderFactory>
    {
        private string _appData;

        [SetUp]
        public void SetUp()
        {
            _appData = GetTempFilePath();
            Directory.CreateDirectory(_appData);

            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(_appData);

            // Back the mock with the real file system so a legacy rename would actually move the files.
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(c => c.FolderExists(It.IsAny<string>())).Returns<string>(Directory.Exists);
            disk.Setup(c => c.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(c => c.EnsureFolder(It.IsAny<string>())).Callback<string>(p => Directory.CreateDirectory(p));
            disk.Setup(c => c.FolderWritable(It.IsAny<string>())).Returns(true);
            disk.Setup(c => c.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((s, d, o) => File.Copy(s, d, o));
            disk.Setup(c => c.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((s, d, o) => File.Move(s, d, o));
            disk.Setup(c => c.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
        }

        [TestCase("nzbdrone.db")]
        [TestCase("whisparr.db")]
        [TestCase("whisparr2.db")]
        [TestCase("whisparr3.db")]
        public void should_leave_legacy_database_untouched(string legacyName)
        {
            var legacyPath = Path.Combine(_appData, legacyName);
            var legacyConfigPath = Path.Combine(_appData, "config.xml");
            var legacyBytes = new byte[] { 1, 2, 3, 4, 5 };
            var configBytes = new byte[] { 9, 8, 7 };

            File.WriteAllBytes(legacyPath, legacyBytes);
            File.WriteAllBytes(legacyConfigPath, configBytes);

            Subject.Register();

            File.ReadAllBytes(legacyPath).Should().Equal(legacyBytes);
            File.ReadAllBytes(legacyConfigPath).Should().Equal(configBytes);
            File.Exists(Path.Combine(_appData, "whisparr5.db")).Should().BeFalse();
        }
    }
}
