using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Backup
{
    [TestFixture]
    public class BackupServiceFixture : CoreTest<BackupService>
    {
        private readonly string _appData = @"C:\Whisparr".AsOsAgnostic();
        private readonly string _temp = @"C:\Temp".AsOsAgnostic();

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.AppDataFolder).Returns(_appData);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.TempFolder).Returns(_temp);
        }

        [Test]
        public void should_restore_database_from_backup_zip()
        {
            var restoreFolder = Path.Combine(_temp, "whisparr_backup_restore");
            var database = Path.Combine(restoreFolder, "whisparr5.db");
            var config = Path.Combine(restoreFolder, "config5.xml");

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(restoreFolder, false))
                  .Returns(new[] { database, config });

            Subject.Restore("backup.zip");

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.MoveFile(database, Path.Combine(_appData, "whisparr5.restore"), true), Times.Once());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.MoveFile(config, Path.Combine(_appData, "config5.xml"), true), Times.Once());
        }

        [TestCase("whisparr_backup_v5.0.0.12_2026.10.16_12.00.00.zip", true)]
        [TestCase("whisparr_backup_v3.5.0.1234_2026.10.16_12.00.00.zip", false)]
        [TestCase("whisparr_backup_v10.0.0.1_2026.10.16_12.00.00.zip", false)]
        [TestCase("nzbdrone_backup_2016.01.01_00.00.00.zip", false)]
        public void should_only_recognise_v5_backups(string fileName, bool expected)
        {
            BackupService.BackupFileRegex.IsMatch(fileName).Should().Be(expected);
        }

        [Test]
        public void should_not_delete_eros_backups_during_retention_cleanup()
        {
            var scheduled = Path.Combine(_appData, "Backups", "scheduled");
            var v3Backup = Path.Combine(scheduled, "whisparr_backup_v3.5.0.1234_2026.01.01_12.00.00.zip");
            var v5Backup = Path.Combine(scheduled, "whisparr_backup_v5.0.0.12_2026.01.01_12.00.00.zip");
            var old = DateTime.UtcNow.AddDays(-365);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderWritable(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IMainDatabase>().SetupGet(s => s.DatabaseType).Returns(DatabaseType.SQLite);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.BackupFolder).Returns("Backups");
            Mocker.GetMock<IConfigService>().SetupGet(s => s.BackupRetention).Returns(28);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(scheduled, false)).Returns(new[] { v3Backup, v5Backup });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileGetLastWrite(It.IsAny<string>())).Returns(old);

            Subject.Backup(BackupType.Scheduled);

            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(v5Backup), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.DeleteFile(v3Backup), Times.Never());
        }

        [TestCase("whisparr3.db")]
        [TestCase("whisparr2.db")]
        [TestCase("whisparr.db")]
        [TestCase("nzbdrone.db")]
        public void should_refuse_to_restore_a_legacy_database(string databaseFileName)
        {
            var restoreFolder = Path.Combine(_temp, "whisparr_backup_restore");
            var database = Path.Combine(restoreFolder, databaseFileName);
            var config = Path.Combine(restoreFolder, "config.xml");

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(restoreFolder, false))
                  .Returns(new[] { database, config });

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore("backup.zip"))
                  .Message.Should().Contain("v5");

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_delete_current_database_journal_when_creating_backup()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderWritable(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IMainDatabase>().SetupGet(s => s.DatabaseType).Returns(DatabaseType.SQLite);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.BackupFolder).Returns("Backups");

            Subject.Backup(BackupType.Manual);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.DeleteFile(Path.Combine(_temp, "whisparr_backup", "whisparr5.db-journal")), Times.Once());
        }
    }
}
