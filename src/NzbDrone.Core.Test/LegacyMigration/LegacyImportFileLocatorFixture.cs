using System.IO;
using FluentAssertions;
using Moq;
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
    public class LegacyImportFileLocatorFixture : CoreTest<LegacyImportFileLocator>
    {
        private string _folder;
        private string _stale;
        private string _actual;
        private ImportFile _file;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\media\Studio\2026-04-29 - Milk".AsOsAgnostic();
            _stale = Path.Combine(_folder, "passion-hd.2026-04-29.milk.mp4");
            _actual = Path.Combine(_folder, "passionhd.2026-04-29.milk.mp4");

            _file = new ImportFile
            {
                Id = 5,
                Path = _stale,
                RelativePath = Path.Combine("Studio", "2026-04-29 - Milk", "passion-hd.2026-04-29.milk.mp4"),
                Name = "passion-hd.2026-04-29.milk",
                OsHash = "stale",
                PHash = "stale"
            };

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_folder)).Returns(true);
        }

        private void GivenVideos(params string[] videos)
        {
            Mocker.GetMock<IDiskScanService>().Setup(s => s.GetVideoFiles(_folder, false)).Returns(videos);
        }

        private void VerifyNotRepointed()
        {
            _file.Path.Should().Be(_stale);
            Mocker.GetMock<IImportFileRepository>().Verify(r => r.Update(It.IsAny<ImportFile>()), Times.Never());
        }

        [Test]
        public void should_return_null_for_no_file()
        {
            Subject.Locate(null).Should().BeNull();
        }

        [Test]
        public void should_not_list_the_folder_when_the_file_exists()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(_stale)).Returns(true);

            Subject.Locate(_file).Should().BeSameAs(_file);

            VerifyNotRepointed();
            Mocker.GetMock<IDiskScanService>().Verify(s => s.GetVideoFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_repoint_to_the_only_video_in_the_folder()
        {
            GivenVideos(_actual);

            Subject.Locate(_file);

            _file.Path.Should().Be(_actual);
            _file.RelativePath.Should().Be(Path.Combine("Studio", "2026-04-29 - Milk", "passionhd.2026-04-29.milk.mp4"));
            _file.Name.Should().Be("passionhd.2026-04-29.milk");
            _file.OsHash.Should().BeNull();
            _file.PHash.Should().BeNull();
            Mocker.GetMock<IImportFileRepository>().Verify(r => r.Update(_file), Times.Once());
        }

        [Test]
        public void should_leave_a_folder_with_several_videos()
        {
            GivenVideos(_actual, Path.Combine(_folder, "other.mp4"));

            Subject.Locate(_file);

            VerifyNotRepointed();
        }

        [Test]
        public void should_leave_a_folder_with_no_videos()
        {
            GivenVideos();

            Subject.Locate(_file);

            VerifyNotRepointed();
        }

        [Test]
        public void should_leave_a_missing_folder()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_folder)).Returns(false);

            Subject.Locate(_file);

            VerifyNotRepointed();
            Mocker.GetMock<IDiskScanService>().Verify(s => s.GetVideoFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_leave_a_video_another_import_file_claims()
        {
            GivenVideos(_actual);
            Mocker.GetMock<IImportFileRepository>().Setup(r => r.FindByPath(_actual)).Returns(new ImportFile { Id = 6, Path = _actual });

            Subject.Locate(_file);

            VerifyNotRepointed();
        }

        [Test]
        public void should_leave_a_video_a_scene_already_has()
        {
            GivenVideos(_actual);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.FindByFullPath(_actual)).Returns(new MovieFile { Id = 6 });

            Subject.Locate(_file);

            VerifyNotRepointed();
        }

        [Test]
        public void should_keep_the_legacy_path_when_the_disk_fails()
        {
            Mocker.GetMock<IDiskScanService>().Setup(s => s.GetVideoFiles(_folder, false)).Throws(new IOException("Stale file handle"));

            Subject.Locate(_file).Should().BeSameAs(_file);

            VerifyNotRepointed();
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
