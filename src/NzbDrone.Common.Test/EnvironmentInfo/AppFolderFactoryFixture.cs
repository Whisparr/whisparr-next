using System.IO;
using FluentAssertions;
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

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderWritable(_appData)).Returns(true);
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
