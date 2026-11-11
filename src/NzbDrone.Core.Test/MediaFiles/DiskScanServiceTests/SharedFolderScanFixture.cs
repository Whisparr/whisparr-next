using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.DiskScanServiceTests
{
    [TestFixture]
    public class SharedFolderScanFixture : CoreTest<DiskScanService>
    {
        private string _rootFolder;
        private string _folder;
        private Movie _s1;
        private Movie _s2;
        private List<ImportDecision> _imported;

        [SetUp]
        public void Setup()
        {
            _rootFolder = @"C:\lib\scenes".AsOsAgnostic();
            _folder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            _s1 = GivenScene(101, _folder);
            _s2 = GivenScene(102, _folder);
            _imported = new List<ImportDecision>();

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderEmpty(It.IsAny<string>())).Returns(false);

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), null))
                  .Returns(_rootFolder);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MovieFile>());

            // The folder pass loads every scene's rows at once; answer from the per-scene setups
            var files = Mocker.GetMock<IMediaFileService>();
            files.Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                 .Returns((IEnumerable<int> ids) => ids.SelectMany(id => files.Object.GetFilesByMovie(id)).ToList());

            // Nothing is tracked unless a test says so
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(It.IsAny<string>(), It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files.ToList());

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);

            Mocker.GetMock<IImportApprovedMovie>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>()))
                  .Callback<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((d, _, _, _) => _imported.AddRange(d))
                  .Returns(new List<ImportResult>());
        }

        private static Movie GivenScene(int id, string path)
        {
            return new Movie
            {
                Id = id,
                Path = path,
                MovieMetadata = new MovieMetadata { ItemType = ItemType.Scene, Title = "Scene " + id }
            };
        }

        private string InFolder(string name) => Path.Combine(_folder, name);

        // Replaces the blanket "everything is shared" mock with the real layout decision
        private void GivenRealLayoutWithFlatJav()
        {
            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(new NamingConfig { SceneFolderFormat = "scenes/{Studio Title}/{Scene Title}", JavFolderFormat = "{Studio Title}" });
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.AllMoviePathEntries())
                  .Returns(new List<MoviePathEntry>());
            Mocker.SetConstant<ISceneFolderLayout>(Mocker.Resolve<SceneFolderLayout>());
        }

        private void GivenFiles(params string[] files)
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true)).Returns(files);
        }

        private static ImportDecision DecisionFor(Movie scene, string file)
        {
            return new ImportDecision(new Parser.Model.LocalMovie { Movie = scene, Path = file });
        }

        [Test]
        public void rescan_of_s1_does_not_import_s2_file_as_s1()
        {
            GivenFiles(InFolder("s1.mp4"), InFolder("s2.mp4"));

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(() => new List<ImportDecision> { DecisionFor(_s1, InFolder("s1.mp4")), DecisionFor(_s2, InFolder("s2.mp4")) });

            Subject.Scan(_s1);

            _imported.Should().HaveCount(2);
            _imported.Single(d => d.LocalMovie.Path == InFolder("s1.mp4")).LocalMovie.Movie.Id.Should().Be(_s1.Id);
            _imported.Single(d => d.LocalMovie.Path == InFolder("s2.mp4")).LocalMovie.Movie.Id.Should().Be(_s2.Id);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void rescan_of_s1_leaves_unidentified_file_alone()
        {
            GivenFiles(InFolder("unknown.mp4"));

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(_s1);

            _imported.Should().BeEmpty();

            Mocker.GetMock<ISharedFolderScanner>()
                  .Verify(v => v.IdentifyOwned(_folder, It.Is<List<string>>(l => l.Single() == InFolder("unknown.mp4"))), Times.Once());
        }

        [Test]
        public void rescan_of_studio_scene_ignores_files_in_nested_old_scene_folder_owned_by_other_scene()
        {
            var nested = Path.Combine(_folder, "old S3 folder");
            var s3 = GivenScene(103, nested);

            GivenFiles(Path.Combine(nested, "s3.mp4"));

            // The scanner drops scenes whose Path isn't the scanned folder, so S1's scan makes no decision for it
            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(_s1);

            _imported.Should().BeEmpty();
            _imported.Any(d => d.LocalMovie.Movie.Id == s3.Id).Should().BeFalse();
        }

        [Test]
        public void rescan_of_flat_scene_ignores_movie_files_in_nested_movie_folder()
        {
            var movieFolder = Path.Combine(_folder, "Some Movie");

            GivenFiles(Path.Combine(movieFolder, "movie.mp4"), Path.Combine(movieFolder, "movie-extra.mp4"));

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(_s1);

            _imported.Should().BeEmpty();
        }

        [Test]
        public void exclusive_folder_still_imports_any_file_as_the_scene()
        {
            var file = InFolder("a8f3c0de.mp4");

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_s1)).Returns(false);
            GivenFiles(file);

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), _s1, false))
                  .Returns(new List<ImportDecision> { DecisionFor(_s1, file) });

            Subject.Scan(_s1);

            _imported.Should().ContainSingle().Which.LocalMovie.Path.Should().Be(file);

            Mocker.GetMock<ISharedFolderScanner>()
                  .Verify(v => v.IdentifyOwned(It.IsAny<Movie>(), It.IsAny<List<string>>()), Times.Never());
            Mocker.GetMock<ISharedFolderScanner>()
                  .Verify(v => v.IdentifyOwned(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never());
        }

        [Test]
        public void unmapped_cleanup_only_removes_rows_for_owned_files()
        {
            var s1File = InFolder("s1.mp4");
            var s2File = InFolder("s2.mp4");
            var ownRow = new MovieFile { Id = 1, MovieId = 0, OriginalFilePath = s1File };
            var siblingRow = new MovieFile { Id = 2, MovieId = 0, OriginalFilePath = s2File };

            GivenFiles(s1File, s2File);

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetUnmappedFiles())
                  .Returns(new List<MovieFile> { ownRow, siblingRow });

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision> { DecisionFor(_s1, s1File) });

            Subject.Scan(_s1);

            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(ownRow), Times.Once());
            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(siblingRow), Times.Never());
        }

        [Test]
        public void rescan_of_flat_jav_folder_keeps_each_file_linked_to_its_own_row()
        {
            var j1 = GivenScene(201, _folder);
            var j2 = GivenScene(202, _folder);
            j1.MovieMetadata.Value.ItemType = ItemType.Jav;
            j2.MovieMetadata.Value.ItemType = ItemType.Jav;

            GivenRealLayoutWithFlatJav();

            var j1File = new MovieFile { Id = 1, MovieId = j1.Id, RelativePath = "j1.mp4" };
            var j2File = new MovieFile { Id = 2, MovieId = j2.Id, RelativePath = "j2.mp4" };

            GivenFiles(InFolder("j1.mp4"), InFolder("j2.mp4"));

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(j1.Id)).Returns(new List<MovieFile> { j1File });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(j2.Id)).Returns(new List<MovieFile> { j2File });

            // Both files are tracked (j1's and j2's rows), as the real any-item filter knows
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files.Where(f => f != InFolder("j1.mp4") && f != InFolder("j2.mp4")).ToList());
            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(It.IsAny<string>(), It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(j1);
            Subject.Scan(j2);

            _imported.Should().BeEmpty();

            // Each scan identifies the folder once and offers neither tracked file, its own or the sibling's
            Mocker.GetMock<ISharedFolderScanner>().Verify(v => v.IdentifyOwned(_folder, It.Is<List<string>>(l => l.Count == 0)), Times.Exactly(2));
            Mocker.GetMock<ISharedFolderScanner>().Verify(v => v.IdentifyOwned(_folder, It.Is<List<string>>(l => l.Count > 0)), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void unmapped_cleanup_in_a_per_release_folder_ignores_a_sibling_folder_with_the_same_prefix()
        {
            var abp12 = GivenScene(201, Path.Combine(_folder, "ABP-12"));
            abp12.MovieMetadata.Value.ItemType = ItemType.Jav;

            var siblingFile = Path.Combine(_folder, "ABP-123", "x.mp4");
            var siblingRow = new MovieFile { Id = 2, MovieId = 0, OriginalFilePath = siblingFile };
            var ownRow = new MovieFile { Id = 3, MovieId = 0, OriginalFilePath = Path.Combine(_folder, "ABP-12", "y.mp4") };

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(abp12)).Returns(false);
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), abp12, false))
                  .Returns(new List<ImportDecision>());
            GivenFiles();
            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetUnmappedFiles())
                  .Returns(new List<MovieFile> { siblingRow, ownRow });

            Subject.Scan(abp12);

            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(siblingRow), Times.Never());
            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(ownRow), Times.Once());
        }

        [Test]
        public void unmapped_cleanup_for_a_flat_jav_folder_only_removes_rows_for_owned_files()
        {
            var j1 = GivenScene(201, _folder);
            j1.MovieMetadata.Value.ItemType = ItemType.Jav;

            GivenRealLayoutWithFlatJav();

            var j1File = InFolder("j1.mp4");
            var j2File = InFolder("j2.mp4");
            var ownRow = new MovieFile { Id = 1, MovieId = 0, OriginalFilePath = j1File };
            var siblingRow = new MovieFile { Id = 2, MovieId = 0, OriginalFilePath = j2File };

            GivenFiles(j1File, j2File);

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetUnmappedFiles())
                  .Returns(new List<MovieFile> { ownRow, siblingRow });

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision> { DecisionFor(j1, j1File) });

            Subject.Scan(j1);

            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(ownRow), Times.Once());
            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(siblingRow), Times.Never());
        }

        [Test]
        public void relink_in_shared_folder_only_considers_unowned_files()
        {
            var s1Missing = new MovieFile { Id = 1, MovieId = _s1.Id, RelativePath = "s1.mp4", OsHash = "same" };
            var s2Tracked = new MovieFile { Id = 2, MovieId = _s2.Id, RelativePath = "s2.mp4", OsHash = "same" };

            GivenFiles(InFolder("s2.mp4"));

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(false);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(_s1.Id)).Returns(new List<MovieFile> { s1Missing });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(_s2.Id)).Returns(new List<MovieFile> { s2Tracked });
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files.Where(f => f != InFolder("s2.mp4")).ToList());
            Mocker.GetMock<IVideoHashService>()
                  .Setup(s => s.ComputeOsHash(It.IsAny<string>()))
                  .Returns("same");
            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(_s1);

            s1Missing.RelativePath.Should().Be("s1.mp4");
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(s1Missing), Times.Never());
        }

        [Test]
        public void relink_in_shared_folder_never_takes_a_nested_items_tracked_file()
        {
            var s1Missing = new MovieFile { Id = 1, MovieId = _s1.Id, RelativePath = "s1.mp4", OsHash = "same" };
            var nestedFile = Path.Combine(_folder, "Nested Movie", "movie.mp4");

            GivenFiles(nestedFile);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(false);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(_s1.Id)).Returns(new List<MovieFile> { s1Missing });

            // The nested item (path below the scanned folder) tracks the file; the any-item filter knows it
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files.Where(f => f != nestedFile).ToList());
            Mocker.GetMock<IVideoHashService>()
                  .Setup(s => s.ComputeOsHash(It.IsAny<string>()))
                  .Returns("same");
            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(_s1);

            s1Missing.RelativePath.Should().Be("s1.mp4");
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(s1Missing), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()), Times.AtLeastOnce());
        }

        [Test]
        public void sibling_scans_of_one_folder_are_serialised_by_the_folder_lock()
        {
            var file = InFolder("s1.mp4");
            var inFirstImport = new ManualResetEventSlim(false);
            var releaseFirstImport = new ManualResetEventSlim(false);
            var identifyCalls = 0;
            var imports = 0;

            GivenFiles(file);

            // The file is only handed out until it has been imported, as the real tracked-file filter does
            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(It.IsAny<string>(), It.IsAny<List<string>>()))
                  .Returns(() =>
                  {
                      Interlocked.Increment(ref identifyCalls);

                      return Volatile.Read(ref imports) == 0
                          ? new List<ImportDecision> { DecisionFor(_s1, file) }
                          : new List<ImportDecision>();
                  });

            Mocker.GetMock<IImportApprovedMovie>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>()))
                  .Callback<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((d, _, _, _) =>
                  {
                      if (d.Count == 0)
                      {
                          return;
                      }

                      inFirstImport.Set();
                      releaseFirstImport.Wait(TimeSpan.FromSeconds(10));
                      Interlocked.Increment(ref imports);
                  })
                  .Returns(new List<ImportResult>());

            var first = Task.Run(() => Subject.Scan(_s1));
            inFirstImport.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

            var second = Task.Run(() => Subject.Scan(_s2));

            // The sibling can't identify while the first scan is still importing
            Thread.Sleep(300);
            Volatile.Read(ref identifyCalls).Should().Be(1);

            releaseFirstImport.Set();
            Task.WaitAll(new[] { first, second }, TimeSpan.FromSeconds(10)).Should().BeTrue();

            identifyCalls.Should().Be(2);
            imports.Should().Be(1);
        }
    }
}
