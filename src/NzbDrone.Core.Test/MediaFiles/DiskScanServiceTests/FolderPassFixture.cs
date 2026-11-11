using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.DiskScanServiceTests
{
    [TestFixture]
    public class FolderPassFixture : CoreTest<DiskScanService>
    {
        private string _rootFolder;
        private string _folder;
        private Movie _s1;
        private Movie _s2;
        private Movie _s3;
        private List<Movie> _movies;
        private List<MoviePathEntry> _entries;
        private List<MovieFile> _rows;
        private List<ImportDecision> _imported;
        private List<MovieScannedEvent> _scanned;
        private List<MovieScanSkippedEvent> _skipped;

        [SetUp]
        public void Setup()
        {
            _rootFolder = @"C:\lib\scenes".AsOsAgnostic();
            _folder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            _movies = new List<Movie>();
            _entries = new List<MoviePathEntry>();
            _rows = new List<MovieFile>();
            _s1 = GivenScene(1, _folder);
            _s2 = GivenScene(2, _folder);
            _s3 = GivenScene(3, _folder);
            _imported = new List<ImportDecision>();
            _scanned = new List<MovieScannedEvent>();
            _skipped = new List<MovieScanSkippedEvent>();

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderEmpty(It.IsAny<string>())).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true)).Returns(Array.Empty<string>());

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), null))
                  .Returns(_rootFolder);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns((int id) => _rows.Where(r => r.MovieId == id).ToList());
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns((IEnumerable<int> ids) => _rows.Where(r => ids.Contains(r.MovieId)).ToList());
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetMovie(It.IsAny<int>()))
                  .Returns((int id) => _rows.Single(r => r.Id == id));
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(It.IsAny<string>(), It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files.ToList());

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.GetEntries(It.IsAny<IEnumerable<int>>()))
                  .Returns((IEnumerable<int> ids) => _entries.Where(e => ids.Contains(e.Id)).ToList());

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns((IEnumerable<int> ids) => _movies.Where(m => ids.Contains(m.Id)).ToList());

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(It.IsAny<string>(), It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision>());

            Mocker.GetMock<IImportApprovedMovie>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>()))
                  .Callback<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((d, _, _, _) => _imported.AddRange(d))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<MovieScannedEvent>()))
                  .Callback<MovieScannedEvent>(e => _scanned.Add(e));
            Mocker.GetMock<IEventAggregator>()
                  .Setup(s => s.PublishEvent(It.IsAny<MovieScanSkippedEvent>()))
                  .Callback<MovieScanSkippedEvent>(e => _skipped.Add(e));
        }

        private Movie GivenScene(int id, string path, ItemType itemType = ItemType.Scene, bool indexed = true)
        {
            var movie = new Movie
            {
                Id = id,
                Path = path,
                MovieMetadata = new MovieMetadata { ItemType = itemType, Title = "Scene " + id }
            };

            _movies.Add(movie);

            if (indexed)
            {
                _entries.Add(new MoviePathEntry { Id = id, Path = path, ItemType = itemType });
            }

            return movie;
        }

        private string InFolder(string name) => Path.Combine(_folder, name);

        private void GivenFiles(params string[] files)
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(_folder, true)).Returns(files);
        }

        private static ImportDecision DecisionFor(Movie scene, string file)
        {
            return new ImportDecision(new Parser.Model.LocalMovie { Movie = scene, Path = file });
        }

        [Test]
        public void folder_is_listed_once_for_three_requested_scenes()
        {
            GivenFiles(InFolder("a.mp4"), InFolder("b.mp4"), InFolder("a.nfo"));

            Subject.Scan(new[] { 1, 2, 3 });

            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(_folder, true), Times.Once());
            Mocker.GetMock<ISharedFolderScanner>().Verify(v => v.IdentifyOwned(_folder, It.IsAny<List<string>>()), Times.Once());
        }

        [Test]
        public void each_requested_scene_gets_one_scanned_event()
        {
            GivenFiles(InFolder("a.mp4"), InFolder("a.nfo"));

            Subject.Scan(new[] { 1, 2, 3 });

            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
            _scanned.Should().OnlyContain(e => e.PossibleExtraFiles.Single() == InFolder("a.nfo"));
        }

        [Test]
        public void single_scene_rescan_finds_new_file_and_finishes_only_that_scene()
        {
            var file = InFolder("new.mp4");
            GivenFiles(file);

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.Is<List<string>>(l => l.Contains(file))))
                  .Returns(new List<ImportDecision> { DecisionFor(_s1, file) });

            Subject.Scan(_s1);

            _imported.Should().ContainSingle().Which.LocalMovie.Movie.Id.Should().Be(_s1.Id);
            _scanned.Should().ContainSingle().Which.Movie.Id.Should().Be(_s1.Id);
        }

        [Test]
        public void short_listing_unlinks_nothing()
        {
            var real = Mocker.Resolve<MediaFileTableCleanupService>();
            Mocker.SetConstant<IMediaFileTableCleanupService>(real);

            _rows.Add(new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "s1.mp4" });
            _rows.Add(new MovieFile { Id = 12, MovieId = _s2.Id, RelativePath = "s2.mp4" });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(true);

            Subject.Scan(new[] { 1, 2, 3 });

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void file_imported_after_listing_is_not_unlinked()
        {
            var real = Mocker.Resolve<MediaFileTableCleanupService>();
            Mocker.SetConstant<IMediaFileTableCleanupService>(real);

            GivenFiles(InFolder("s1.mp4"));

            _rows.Add(new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "late.mp4" });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(InFolder("late.mp4"))).Returns(true);

            Subject.Scan(new[] { 1, 2, 3 });

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void file_rows_are_loaded_once_for_three_requested_scenes()
        {
            var real = Mocker.Resolve<MediaFileTableCleanupService>();
            Mocker.SetConstant<IMediaFileTableCleanupService>(real);

            var kept = new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "s1.mp4", Size = 5 };
            var gone = new MovieFile { Id = 12, MovieId = _s2.Id, RelativePath = "gone.mp4" };
            _rows.Add(kept);
            _rows.Add(gone);

            GivenFiles(InFolder("s1.mp4"));
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(InFolder("s1.mp4"))).Returns(5);

            Subject.Scan(new[] { 1, 2, 3 });

            Mocker.GetMock<IMediaFileService>().Verify(v => v.GetFilesByMovies(It.IsAny<IEnumerable<int>>()), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.GetFilesByMovie(It.IsAny<int>()), Times.Never());

            // The preloaded rows reach the cleanup (still confirmed on disk) and the existing-file update
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(gone, DeleteMediaFileReason.MissingFromDisk), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(kept, It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFileSize(InFolder("s1.mp4")), Times.AtLeastOnce());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFileSize(InFolder("gone.mp4")), Times.Never());
        }

        [Test]
        public void tracked_file_filter_runs_once_per_folder()
        {
            GivenFiles(InFolder("a.mp4"));

            Subject.Scan(new[] { 1, 2, 3 });

            Mocker.GetMock<IMediaFileService>().Verify(v => v.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()), Times.Once());
        }

        [Test]
        public void renamed_row_is_seen_by_the_existing_file_update()
        {
            var row = new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "old.mp4", OsHash = "abc", Size = 5 };
            _rows.Add(row);

            GivenFiles(InFolder("new.mp4"));
            Mocker.GetMock<IVideoHashService>()
                  .Setup(s => s.ComputeOsHash(InFolder("new.mp4")))
                  .Returns("abc");
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(InFolder("new.mp4"))).Returns(5);

            Subject.Scan(new[] { 1, 2, 3 });

            row.RelativePath.Should().Be("new.mp4");
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFileSize(InFolder("old.mp4")), Times.Never());
        }

        [Test]
        public void missing_root_folder_skips_every_requested_scene()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(It.IsAny<string>())).Returns(false);

            Subject.Scan(new[] { 1, 2, 3 });

            _skipped.Should().HaveCount(3);
            _skipped.Should().OnlyContain(e => e.Reason == MovieScanSkippedReason.RootFolderDoesNotExist);
            _skipped.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
            _scanned.Should().BeEmpty();
            Mocker.GetMock<IMediaFileTableCleanupService>().Verify(v => v.Clean(It.IsAny<Movie>(), It.IsAny<List<string>>(), It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void empty_root_folder_skips_every_requested_scene()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_folder)).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderEmpty(_rootFolder)).Returns(true);

            Subject.Scan(new[] { 1, 2, 3 });

            _skipped.Should().HaveCount(3);
            _skipped.Should().OnlyContain(e => e.Reason == MovieScanSkippedReason.RootFolderIsEmpty);
            _skipped.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
            Mocker.GetMock<IMediaFileTableCleanupService>().Verify(v => v.Clean(It.IsAny<Movie>(), It.IsAny<List<string>>(), It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void unmapped_rows_for_all_imported_files_are_removed()
        {
            var a = InFolder("a.mp4");
            var b = InFolder("b.mp4");
            var rowA = new MovieFile { Id = 21, MovieId = 0, OriginalFilePath = a };
            var rowB = new MovieFile { Id = 22, MovieId = 0, OriginalFilePath = b };
            var other = new MovieFile { Id = 23, MovieId = 0, OriginalFilePath = InFolder("c.mp4") };

            GivenFiles(a, b);

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetUnmappedFiles())
                  .Returns(new List<MovieFile> { rowA, rowB, other });
            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<ImportDecision> { DecisionFor(_s1, a), DecisionFor(_s2, b) });

            Subject.Scan(new[] { 1, 2, 3 });

            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(rowA), Times.Once());
            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(rowB), Times.Once());
            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.Delete(other), Times.Never());
            Mocker.GetMock<IMediaFileRepository>().Verify(v => v.GetUnmappedFiles(), Times.Once());
        }

        [Test]
        public void exclusive_scene_and_movie_take_the_old_path()
        {
            var movie = GivenScene(5, @"C:\lib\movies\Some Movie".AsOsAgnostic(), ItemType.Movie);
            var file = Path.Combine(movie.Path, "movie.mp4");

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(movie.Path, true)).Returns(new[] { file });
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), false))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(new[] { 1, 5 });

            Mocker.GetMock<ISharedFolderScanner>().Verify(v => v.IdentifyOwned(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never());
            Mocker.GetMock<ISharedFolderScanner>().Verify(v => v.IdentifyOwned(It.IsAny<Movie>(), It.IsAny<List<string>>()), Times.Never());
            Mocker.GetMock<IMakeImportDecision>().Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), movie, false), Times.Once());
            Mocker.GetMock<IMakeImportDecision>().Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), _s1, false), Times.Once());
            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 1, 5 });
        }

        [Test]
        public void id_missing_from_path_index_is_still_scanned()
        {
            GivenScene(4, _folder, indexed: false);

            Subject.Scan(new[] { 1, 4, 99 });

            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 1, 4 });
        }

        [Test]
        public void failing_folder_does_not_stop_the_next()
        {
            var other = @"C:\lib\scenes\Other Studio".AsOsAgnostic();
            GivenScene(7, other);
            GivenScene(8, other);

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(_folder, true)).Throws(new IOException("mount went away"));
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(other, true)).Returns(Array.Empty<string>());

            Subject.Scan(new[] { 1, 2, 7, 8 });

            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 7, 8 });
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void failing_scene_does_not_stop_its_siblings()
        {
            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Setup(s => s.Clean(_s2, It.IsAny<List<string>>(), It.IsAny<List<MovieFile>>()))
                  .Throws(new InvalidOperationException("boom"));

            Subject.Scan(new[] { 1, 2, 3 });

            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 1, 3 });
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void failing_sub_group_does_not_stop_the_rest_of_its_index_group()
        {
            // The cached index still lists 10 and 11 in _folder, but 10 has moved to another shared folder and 11 to its own
            var moved = @"C:\lib\scenes\Moved".AsOsAgnostic();
            var s10 = GivenScene(10, moved);
            var s11 = GivenScene(11, @"C:\lib\scenes\Own Folder".AsOsAgnostic());
            _entries.Single(e => e.Id == s10.Id).Path = _folder;
            _entries.Single(e => e.Id == s11.Id).Path = _folder;

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(s11)).Returns(false);
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), s11, false))
                  .Returns(new List<ImportDecision>());
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(_folder, true)).Throws(new IOException("mount went away"));
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(moved, true)).Returns(Array.Empty<string>());

            Subject.Scan(new[] { 1, 2, 10, 11 });

            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 10, 11 });
            Mocker.GetMock<IMakeImportDecision>().Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), s11, false), Times.Once());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void nested_scene_folder_keeps_its_files()
        {
            var nestedFolder = Path.Combine(_folder, "Sub");
            var nestedFile = Path.Combine(nestedFolder, "n.mp4");
            var n = GivenScene(9, nestedFolder);
            var real = Mocker.Resolve<MediaFileTableCleanupService>();
            Mocker.SetConstant<IMediaFileTableCleanupService>(real);

            GivenFiles(InFolder("a.mp4"), nestedFile);

            _rows.Add(new MovieFile { Id = 31, MovieId = n.Id, RelativePath = "n.mp4" });
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files.Where(f => f != nestedFile).ToList());

            Subject.Scan(new[] { 1, 2, 3 });

            Mocker.GetMock<ISharedFolderScanner>()
                  .Verify(v => v.IdentifyOwned(_folder, It.Is<List<string>>(l => l.Contains(nestedFile))), Times.Never());
            Mocker.GetMock<ISharedFolderScanner>()
                  .Verify(v => v.IdentifyOwned(_folder, It.Is<List<string>>(l => l.Single() == InFolder("a.mp4"))), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void jav_in_shared_folder_uses_folder_pass()
        {
            var jav = GivenScene(6, _folder, ItemType.Jav);

            Subject.Scan(jav);

            Mocker.GetMock<ISharedFolderScanner>().Verify(v => v.IdentifyOwned(_folder, It.IsAny<List<string>>()), Times.Once());
            Mocker.GetMock<IMakeImportDecision>().Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), It.IsAny<bool>()), Times.Never());
            _scanned.Should().ContainSingle().Which.Movie.Id.Should().Be(jav.Id);
        }

        [Test]
        public void items_alone_in_their_folder_are_loaded_in_chunks()
        {
            var ids = Enumerable.Range(1000, 2500).ToList();
            ids.ForEach(id => GivenScene(id, Path.Combine(@"C:\lib\movies".AsOsAgnostic(), "Movie " + id), ItemType.Movie));

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(false);
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), false))
                  .Returns(new List<ImportDecision>());

            Subject.Scan(ids);

            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovies(It.IsAny<IEnumerable<int>>()), Times.Exactly(3));
            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(ids);
        }

        [Test]
        public void rescan_without_id_scans_every_item_through_the_bulk_pass()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.AllMovieIds()).Returns(new List<int> { 1, 2, 3 });

            Subject.Execute(new Core.MediaFiles.Commands.RescanMovieCommand());

            Mocker.GetMock<ISceneFolderLayout>().Verify(v => v.GetEntries(It.Is<IEnumerable<int>>(i => i.SequenceEqual(new[] { 1, 2, 3 }))), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(_folder, true), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(v => v.GetAllMovies(), Times.Never());
            _scanned.Select(e => e.Movie.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }
    }
}
