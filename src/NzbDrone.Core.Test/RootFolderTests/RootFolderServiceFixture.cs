using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.Fingerprints.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class RootFolderServiceFixture : CoreTest<RootFolderService>
    {
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;

            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderWritable(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<RootFolder>());

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig())
                  .Returns(_namingConfig);
        }

        private void WithNonExistingFolder()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(m => m.FolderExists(It.IsAny<string>()))
                .Returns(false);
        }

        [TestCase("D:\\TV Shows\\")]
        [TestCase("//server//folder")]
        public void should_be_able_to_add_root_dir(string path)
        {
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string>());

            var root = new RootFolder { Path = path.AsOsAgnostic() };

            Subject.Add(root);

            Mocker.GetMock<IRootFolderRepository>().Verify(c => c.Insert(root), Times.Once());
        }

        [Test]
        public void should_throw_if_folder_being_added_doesnt_exist()
        {
            WithNonExistingFolder();

            Assert.Throws<DirectoryNotFoundException>(() => Subject.Add(new RootFolder { Path = "C:\\TEST".AsOsAgnostic() }));
        }

        [Test]
        public void should_be_able_to_remove_root_dir()
        {
            Subject.Remove(1);
            Mocker.GetMock<IRootFolderRepository>().Verify(c => c.Delete(1), Times.Once());
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("BAD PATH")]
        public void invalid_folder_path_throws_on_add(string path)
        {
            Assert.Throws<ArgumentException>(() =>
                    Mocker.Resolve<RootFolderService>().Add(new RootFolder { Id = 0, Path = path }));
        }

        [Test]
        public void adding_duplicated_root_folder_should_throw()
        {
            Mocker.GetMock<IRootFolderRepository>().Setup(c => c.All()).Returns(new List<RootFolder> { new RootFolder { Path = "C:\\TV".AsOsAgnostic() } });

            Assert.Throws<InvalidOperationException>(() => Subject.Add(new RootFolder { Path = @"C:\TV".AsOsAgnostic() }));
        }

        [Test]
        public void refresh_should_insert_new_import_files_unhashed_and_queue_the_backfill()
        {
            var root = new RootFolder { Id = 7, Path = @"C:\TV".AsOsAgnostic() };
            var importFolder = Path.Combine(root.Path, "import");
            var known = Path.Combine(importFolder, "known.mp4");
            var added = Path.Combine(importFolder, "added.mp4");
            List<ImportFile> inserted = null;

            SetupRefresh(root, importFolder, new[] { known, added }, known);
            Mocker.GetMock<IImportFileRepository>()
                  .Setup(s => s.InsertMany(It.IsAny<IList<ImportFile>>()))
                  .Callback<IList<ImportFile>>(files => inserted = files.ToList());

            Subject.Refresh(7);

            inserted.Should().ContainSingle();
            inserted[0].Path.Should().Be(added);
            inserted[0].OsHash.Should().BeNull();
            inserted[0].HashedFileModifiedAt.Should().BeNull();
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<GenerateOsHashesCommand>(), CommandPriority.Low, CommandTrigger.Unspecified), Times.Once());
        }

        private (RootFolder Root, string Parked) GivenParkedFile()
        {
            var root = new RootFolder { Id = 7, Path = @"C:\TV".AsOsAgnostic() };
            var importFolder = Path.Combine(root.Path, "import");
            var parked = Path.Combine(root.Path, "Studio", "scene.mp4");

            SetupRefresh(root, importFolder, Array.Empty<string>(), parked);

            return (root, parked);
        }

        private void VerifyDeletedCount(int count)
        {
            Mocker.GetMock<IImportFileRepository>()
                  .Verify(s => s.DeleteMany(It.Is<List<int>>(ids => ids.Count == count)), Times.Once());
        }

        [Test]
        public void refresh_should_keep_a_file_parked_outside_the_import_folder()
        {
            var (_, parked) = GivenParkedFile();
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(parked, It.IsAny<StringComparison>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(parked)).Returns(true);

            Subject.Refresh(7);

            VerifyDeletedCount(0);
        }

        [Test]
        public void refresh_should_forget_a_parked_file_that_is_gone()
        {
            GivenParkedFile();

            Subject.Refresh(7);

            VerifyDeletedCount(1);
        }

        [Test]
        public void refresh_should_keep_a_dead_parked_file_while_a_migration_runs()
        {
            GivenParkedFile();
            Mocker.GetMock<ILegacyMigrationService>().Setup(s => s.IsIncomplete()).Returns(true);

            Subject.Refresh(7);

            VerifyDeletedCount(0);
        }

        [Test]
        public void refresh_should_still_forget_a_file_gone_from_the_import_folder()
        {
            var root = new RootFolder { Id = 7, Path = @"C:\TV".AsOsAgnostic() };
            var importFolder = Path.Combine(root.Path, "import");

            SetupRefresh(root, importFolder, Array.Empty<string>(), Path.Combine(importFolder, "gone.mp4"));
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(importFolder)).Returns(true);

            Subject.Refresh(7);

            VerifyDeletedCount(1);
        }

        [Test]
        public void refresh_should_not_queue_the_backfill_when_no_files_were_added()
        {
            var root = new RootFolder { Id = 7, Path = @"C:\TV".AsOsAgnostic() };
            var importFolder = Path.Combine(root.Path, "import");
            var known = Path.Combine(importFolder, "known.mp4");

            SetupRefresh(root, importFolder, new[] { known }, known);

            Subject.Refresh(7);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<GenerateOsHashesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        private void SetupRefresh(RootFolder root, string importFolder, string[] filesOnDisk, string knownPath)
        {
            Mocker.GetMock<IRootFolderRepository>().Setup(s => s.Get(7)).Returns(root);
            Mocker.GetMock<IMovieRepository>().Setup(s => s.AllMoviePaths()).Returns(new Dictionary<int, string>());
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(importFolder, true)).Returns(filesOnDisk);
            Mocker.GetMock<IImportFileRepository>()
                  .Setup(s => s.FindByRootFolderId(7))
                  .Returns(new List<ImportFile> { new() { Id = 1, RootFolderId = 7, Path = knownPath } });
        }

        [Test]
        public void should_throw_when_adding_not_writable_folder()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderWritable(It.IsAny<string>()))
                  .Returns(false);

            Assert.Throws<UnauthorizedAccessException>(() => Subject.Add(new RootFolder { Path = @"C:\TV".AsOsAgnostic() }));
        }
    }
}
