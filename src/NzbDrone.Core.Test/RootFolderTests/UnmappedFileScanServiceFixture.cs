using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.RootFolders.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class UnmappedFileScanServiceFixture : CoreTest<UnmappedFileScanService>
    {
        private const string Root = "/media";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IRootFolderRepository>().Setup(r => r.All()).Returns(new List<RootFolder> { new RootFolder { Id = 1, Path = Root } });
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(Root)).Returns(true);
            Mocker.GetMock<IMovieRepository>().Setup(r => r.AllMoviePaths()).Returns(new Dictionary<int, string> { { 5, "/media/Tracked" } });
            Mocker.GetMock<IMediaFileRepository>().Setup(r => r.GetAllFiles()).Returns(new List<MovieFile>
            {
                new MovieFile { MovieId = 5, RelativePath = "linked.mp4" },
                new MovieFile { MovieId = 0, RelativePath = "orphan.mp4" }
            });
            Mocker.GetMock<IImportFileRepository>().Setup(r => r.FindByRootFolderId(1)).Returns(new List<ImportFile>());
            Mocker.GetMock<IDiskScanService>()
                .Setup(d => d.FilterPaths(Root, It.IsAny<IEnumerable<string>>(), false))
                .Returns((string _, IEnumerable<string> paths, bool _) => paths.ToList());
        }

        private void GivenFiles(params string[] files)
        {
            Mocker.GetMock<IDiskScanService>().Setup(d => d.GetVideoFiles(Root, true)).Returns(files);
        }

        private void VerifyInserted(params string[] paths)
        {
            Mocker.GetMock<IImportFileRepository>().Verify(
                r => r.InsertMany(It.Is<IList<ImportFile>>(l => l.Select(f => f.Path).SequenceEqual(paths))),
                Times.Once);
        }

        [Test]
        public void should_add_only_files_nothing_tracks()
        {
            GivenFiles("/media/Tracked/linked.mp4", "/media/Other/new.mp4");

            Subject.Execute(new ScanUnmappedFilesCommand());

            VerifyInserted("/media/Other/new.mp4");
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<GenerateOsHashesCommand>(), CommandPriority.Low, It.IsAny<CommandTrigger>()), Times.Once);
        }

        [Test]
        public void should_not_bring_back_a_dismissed_file()
        {
            Mocker.GetMock<IImportFileRepository>().Setup(r => r.FindByRootFolderId(1)).Returns(new List<ImportFile>
            {
                new ImportFile { Id = 3, Path = "/media/Other/gone-away.mp4", Dismissed = true }
            });
            GivenFiles("/media/Other/gone-away.mp4");

            Subject.Execute(new ScanUnmappedFilesCommand());

            VerifyInserted();
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<GenerateOsHashesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never);
        }

        [Test]
        public void should_remove_rows_for_files_linked_since()
        {
            Mocker.GetMock<IImportFileRepository>().Setup(r => r.FindByRootFolderId(1)).Returns(new List<ImportFile>
            {
                new ImportFile { Id = 3, Path = "/media/Tracked/linked.mp4" },
                new ImportFile { Id = 4, Path = "/media/Other/still.mp4" }
            });
            GivenFiles("/media/Tracked/linked.mp4", "/media/Other/still.mp4");

            Subject.Execute(new ScanUnmappedFilesCommand());

            Mocker.GetMock<IImportFileRepository>().Verify(r => r.DeleteMany(It.Is<List<int>>(l => l.SequenceEqual(new[] { 3 }))));
        }

        [Test]
        public void should_do_nothing_while_a_migration_is_running()
        {
            Mocker.GetMock<ILegacyMigrationService>().Setup(s => s.IsIncomplete()).Returns(true);

            Subject.Execute(new ScanUnmappedFilesCommand());

            Mocker.GetMock<IDiskScanService>().VerifyNoOtherCalls();
            Mocker.GetMock<IImportFileRepository>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_skip_an_unreachable_root_folder()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(Root)).Returns(false);

            Subject.Execute(new ScanUnmappedFilesCommand());

            Mocker.GetMock<IDiskScanService>().Verify(d => d.GetVideoFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }
    }
}
