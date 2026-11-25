using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class DatabaseRestorationServiceFixture : CoreTest<DatabaseRestorationService>
    {
        private string _database;
        private string _restore;

        [SetUp]
        public void Setup()
        {
            WithTempAsAppPath();

            _database = TestFolderInfo.GetDatabase();
            _restore = TestFolderInfo.GetDatabaseRestore();

            // Back the disk provider with the real file system so the test sees what actually happens to the files
            var diskProvider = Mocker.GetMock<IDiskProvider>();
            diskProvider.Setup(s => s.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            diskProvider.Setup(s => s.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
            diskProvider.Setup(s => s.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                        .Callback<string, string, bool>(File.Move);

            SqliteVersionInfoDatabase.Create(_database, 247, 300);
        }

        [Test]
        public void should_leave_the_v5_database_untouched_when_the_restore_is_legacy()
        {
            var before = File.ReadAllBytes(_database);
            SqliteVersionInfoDatabase.Create(_restore, 247);

            Subject.Restore();

            File.ReadAllBytes(_database).Should().Equal(before);
            File.Exists(_restore).Should().BeFalse();
            File.Exists(_restore + ".rejected").Should().BeTrue();
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_leave_the_v5_database_untouched_when_the_restore_is_not_a_database()
        {
            var before = File.ReadAllBytes(_database);
            File.WriteAllText(_restore, "not a database, just some text that is long enough to fill a header");

            Subject.Restore();

            File.ReadAllBytes(_database).Should().Equal(before);
            File.Exists(_restore + ".rejected").Should().BeTrue();
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_replace_the_database_with_a_v5_restore()
        {
            SqliteVersionInfoDatabase.Create(_restore, 247, 300, 301);
            var restored = File.ReadAllBytes(_restore);

            Subject.Restore();

            File.ReadAllBytes(_database).Should().Equal(restored);
            File.Exists(_restore).Should().BeFalse();
        }

        [Test]
        public void should_keep_the_current_database_and_sidecars_as_pre_restore()
        {
            var before = File.ReadAllBytes(_database);
            File.WriteAllText(_database + "-wal", "wal");
            SqliteVersionInfoDatabase.Create(_restore, 247, 300, 301);

            Subject.Restore();

            File.ReadAllBytes(_database + ".pre-restore").Should().Equal(before);
            File.ReadAllText(_database + ".pre-restore-wal").Should().Be("wal");
            File.Exists(_database + "-wal").Should().BeFalse();
        }

        [Test]
        public void should_replace_a_stale_pre_restore_database()
        {
            var before = File.ReadAllBytes(_database);
            File.WriteAllText(_database + ".pre-restore", "stale");
            File.WriteAllText(_database + ".pre-restore-wal", "stale");
            SqliteVersionInfoDatabase.Create(_restore, 247, 300, 301);

            Subject.Restore();

            File.ReadAllBytes(_database + ".pre-restore").Should().Equal(before);
            File.Exists(_database + ".pre-restore-wal").Should().BeFalse();
        }

        [Test]
        public void should_move_the_current_database_back_if_the_restore_fails()
        {
            var before = File.ReadAllBytes(_database);
            File.WriteAllText(_database + "-wal", "wal");
            SqliteVersionInfoDatabase.Create(_restore, 247, 300, 301);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.MoveFile(_restore, _database, It.IsAny<bool>()))
                  .Throws(new IOException());

            Assert.Throws<IOException>(() => Subject.Restore());

            File.ReadAllBytes(_database).Should().Equal(before);
            File.ReadAllText(_database + "-wal").Should().Be("wal");
            File.Exists(_database + ".pre-restore").Should().BeFalse();
            File.Exists(_database + ".pre-restore-wal").Should().BeFalse();
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_delete_the_pre_restore_files_after_startup()
        {
            File.WriteAllText(_database + ".pre-restore", "old");
            File.WriteAllText(_database + ".pre-restore-wal", "old");

            Subject.HandleAsync(new ApplicationStartedEvent());

            File.Exists(_database + ".pre-restore").Should().BeFalse();
            File.Exists(_database + ".pre-restore-wal").Should().BeFalse();
            File.Exists(_database).Should().BeTrue();
        }
    }
}
