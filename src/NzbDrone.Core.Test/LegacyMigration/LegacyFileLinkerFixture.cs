using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacyFileLinkerFixture : CoreTest<LegacyFileLinker>
    {
        private string _root;
        private string _file;
        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _root = "/data/library".AsOsAgnostic();
            _file = "/data/library/Studio/scene.mp4".AsOsAgnostic();
            _movie = new Movie { Id = 7, Path = "/data/library/Studio".AsOsAgnostic() };

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(_file)).Returns(true);

            // The real service holds the locks around the action; here it only has to run it
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.RunLocked(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action>()))
                  .Callback<Movie, string, Action>((_, _, action) => action());

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Add(It.IsAny<MovieFile>()))
                  .Returns<MovieFile>(f =>
                  {
                      f.Id = 99;
                      return f;
                  });

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.All())
                  .Returns(new List<RootFolder> { new RootFolder { Id = 3, Path = _root } });

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), It.IsAny<List<RootFolder>>()))
                  .Returns<string, List<RootFolder>>((p, r) => p.StartsWith(_root) ? _root : System.IO.Path.GetDirectoryName(p));
        }

        [Test]
        public void links_file_in_place_with_confirmed_tier()
        {
            MovieFile added = null;
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Add(It.IsAny<MovieFile>()))
                  .Callback<MovieFile>(f => added = f)
                  .Returns<MovieFile>(f =>
                  {
                      f.Id = 99;
                      return f;
                  });

            var filled = false;

            Subject.Link(_movie, _file, f => filled = true).Should().Be(LinkOutcome.Linked);

            filled.Should().BeTrue();
            added.Path.Should().Be(_file);
            added.RelativePath.Should().Be("scene.mp4");
            added.MovieId.Should().Be(7);
            added.MatchTier.Should().Be(MatchTier.Confirmed);
            added.MediaInfo.Should().BeNull();
            _movie.MovieFileId.Should().Be(99);
            _movie.Path.Should().Be("/data/library/Studio".AsOsAgnostic());

            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(_movie), Times.Once());

            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Verify(d => d.FolderExists(_file), Times.Once());
            disk.Verify(d => d.FileExists(_file), Times.Once());
            disk.VerifyNoOtherCalls();
            Mocker.GetMock<IDiskTransferService>().VerifyNoOtherCalls();
        }

        [Test]
        public void repoints_movie_path_to_file_directory()
        {
            _movie.Path = "/elsewhere/Old".AsOsAgnostic();

            MovieFile added = null;
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Add(It.IsAny<MovieFile>()))
                  .Callback<MovieFile>(f => added = f)
                  .Returns<MovieFile>(f => f);

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.Linked);

            _movie.Path.Should().Be("/data/library/Studio".AsOsAgnostic());
            added.RelativePath.Should().Be("scene.mp4");
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(_movie), Times.Once());
        }

        [Test]
        public void directory_path_is_flagged()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_file)).Returns(true);

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.IsDirectory);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void missing_file_is_flagged()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(_file)).Returns(false);

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.Missing);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void already_linked_same_movie_is_noop()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FindByFullPath(_file))
                  .Returns(new MovieFile { Id = 5, MovieId = 7, Path = _file, RelativePath = "scene.mp4" });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.AlreadyLinked);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [TestCase(MatchTier.Unknown)]
        [TestCase(MatchTier.Name)]
        public void already_linked_below_confirmed_is_confirmed(MatchTier tier)
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FindByFullPath(_file))
                  .Returns(new MovieFile { Id = 5, MovieId = 7, Path = _file, RelativePath = "scene.mp4", MatchTier = tier });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.AlreadyLinked);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.SaveMatchTiers(It.Is<List<MovieFile>>(l => l.Count == 1 && l[0].Id == 5 && l[0].MatchTier == MatchTier.Confirmed)), Times.Once());
        }

        [TestCase(MatchTier.Hash)]
        [TestCase(MatchTier.Manual)]
        [TestCase(MatchTier.Conflict)]
        [TestCase(MatchTier.Confirmed)]
        public void already_linked_at_or_above_confirmed_is_untouched(MatchTier tier)
        {
            var existing = new MovieFile { Id = 5, MovieId = 7, Path = _file, RelativePath = "scene.mp4", MatchTier = tier };

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FindByFullPath(_file))
                  .Returns(existing);

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.AlreadyLinked);

            existing.MatchTier.Should().Be(tier);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.SaveMatchTiers(It.IsAny<List<MovieFile>>()), Times.Never());
        }

        [TestCase(MatchTier.Hash)]
        [TestCase(MatchTier.Manual)]
        [TestCase(MatchTier.Conflict)]
        [TestCase(MatchTier.Confirmed)]
        public void linked_to_other_movie_at_or_above_hash_is_left_alone(MatchTier tier)
        {
            var existing = new MovieFile { Id = 5, MovieId = 8, Path = _file, RelativePath = "scene.mp4", MatchTier = tier };

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FindByFullPath(_file))
                  .Returns(existing);

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.LinkedElsewhere);

            existing.MovieId.Should().Be(8);
            existing.MatchTier.Should().Be(tier);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.SaveMatchTiers(It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [TestCase(MatchTier.Unknown)]
        [TestCase(MatchTier.Name)]
        public void file_guessed_onto_another_scene_moves_to_the_v2_target(MatchTier tier)
        {
            var existing = new MovieFile { Id = 5, MovieId = 8, Path = _file, RelativePath = "old/scene.mp4", MatchTier = tier };
            var oldMovie = new Movie { Id = 8, Path = "/data/library/Other".AsOsAgnostic(), MovieFileId = 5 };

            Mocker.GetMock<IMediaFileService>().Setup(s => s.FindByFullPath(_file)).Returns(existing);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.Is<IEnumerable<int>>(ids => ids.Single() == 8))).Returns(new List<Movie> { oldMovie });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.Linked);

            existing.MovieId.Should().Be(7);
            existing.MatchTier.Should().Be(MatchTier.Confirmed);
            existing.RelativePath.Should().Be("scene.mp4");
            oldMovie.MovieFileId.Should().Be(0);
            _movie.MovieFileId.Should().Be(5);
            _movie.Path.Should().Be("/data/library/Studio".AsOsAgnostic());

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(existing), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(oldMovie), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(_movie), Times.Once());
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void moving_a_file_leaves_the_old_scenes_other_file_alone()
        {
            var existing = new MovieFile { Id = 5, MovieId = 8, Path = _file, RelativePath = "scene.mp4", MatchTier = MatchTier.Name };
            var oldMovie = new Movie { Id = 8, Path = _movie.Path, MovieFileId = 6 };

            Mocker.GetMock<IMediaFileService>().Setup(s => s.FindByFullPath(_file)).Returns(existing);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>())).Returns(new List<Movie> { oldMovie });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.Linked);

            oldMovie.MovieFileId.Should().Be(6);
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(oldMovie), Times.Never());
        }

        [Test]
        public void scene_with_a_different_file_is_not_repointed()
        {
            _movie.MovieFileId = 40;
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetMovie(40))
                  .Returns(new MovieFile { Id = 40, MovieId = 7, Path = "/data/library/Studio/first.mp4".AsOsAgnostic() });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.SceneHasOtherFile);

            _movie.MovieFileId.Should().Be(40);
            _movie.Path.Should().Be("/data/library/Studio".AsOsAgnostic());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void scene_file_without_a_loaded_path_is_compared_by_its_full_path()
        {
            _movie.MovieFileId = 40;
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetMovie(40))
                  .Returns(new MovieFile { Id = 40, MovieId = 7, Path = null, RelativePath = "other.mp4" });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.SceneHasOtherFile);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void scene_file_without_a_loaded_path_that_is_the_same_file_is_already_linked()
        {
            _movie.MovieFileId = 40;
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetMovie(40))
                  .Returns(new MovieFile { Id = 40, MovieId = 7, Path = null, RelativePath = "scene.mp4" });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.AlreadyLinked);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void scene_whose_recorded_file_row_is_gone_takes_the_new_file()
        {
            _movie.MovieFileId = 40;
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetMovie(40)).Returns((MovieFile)null);

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.Linked);

            _movie.MovieFileId.Should().Be(99);
        }

        [Test]
        public void name_guessed_file_is_not_moved_onto_a_scene_that_has_another_file()
        {
            var existing = new MovieFile { Id = 5, MovieId = 8, Path = _file, RelativePath = "scene.mp4", MatchTier = MatchTier.Name };

            _movie.MovieFileId = 40;
            Mocker.GetMock<IMediaFileService>().Setup(s => s.FindByFullPath(_file)).Returns(existing);
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetMovie(40))
                  .Returns(new MovieFile { Id = 40, MovieId = 7, Path = "/data/library/Studio/first.mp4".AsOsAgnostic() });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.SceneHasOtherFile);

            existing.MovieId.Should().Be(8);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void link_runs_under_the_scan_locks_of_the_scene_and_its_folder()
        {
            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.Linked);

            Mocker.GetMock<IDiskScanService>()
                  .Verify(s => s.RunLocked(_movie, "/data/library/Studio".AsOsAgnostic(), It.IsAny<Action>()), Times.Once());
        }

        [Test]
        public void folder_lock_falls_back_to_the_files_directory_when_the_movie_path_is_elsewhere()
        {
            _movie.Path = "/elsewhere/Old".AsOsAgnostic();

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.Linked);

            Mocker.GetMock<IDiskScanService>()
                  .Verify(s => s.RunLocked(_movie, "/data/library/Studio".AsOsAgnostic(), It.IsAny<Action>()), Times.Once());
        }

        [Test]
        public void missing_file_never_takes_the_locks()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(_file)).Returns(false);

            Subject.Link(_movie, _file, f => { });

            Mocker.GetMock<IDiskScanService>().Verify(s => s.RunLocked(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action>()), Times.Never());
        }

        [Test]
        public void park_writes_import_file_and_unmatched_report()
        {
            ImportFile added = null;
            Mocker.GetMock<IImportFileRepository>()
                  .Setup(s => s.Insert(It.IsAny<ImportFile>()))
                  .Callback<ImportFile>(f => added = f)
                  .Returns<ImportFile>(f => f);

            Subject.Park(_file, 4, "Files", "No match").Should().BeTrue();

            added.RootFolderId.Should().Be(3);
            added.Path.Should().Be(_file);
            added.RelativePath.Should().Be("Studio/scene.mp4".AsOsAgnostic());
            added.Name.Should().Be("scene.mp4");
            added.QualityProfileId.Should().Be(4);

            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.UnmatchedFile, "Files", _file, "No match", 1), Times.Once());
        }

        [Test]
        public void park_outside_root_folders_reports_needs_attention()
        {
            var outside = "/other/place/scene.mp4".AsOsAgnostic();
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(outside)).Returns(true);

            Subject.Park(outside, 4, "Files", "No match").Should().BeFalse();

            Mocker.GetMock<IImportFileRepository>().Verify(s => s.Insert(It.IsAny<ImportFile>()), Times.Never());
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Files", outside, It.IsAny<string>(), 1), Times.Once());
        }

        [Test]
        public void park_missing_file_reports_needs_attention()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(_file)).Returns(false);

            Subject.Park(_file, 4, "Files", "No match").Should().BeFalse();

            Mocker.GetMock<IImportFileRepository>().Verify(s => s.Insert(It.IsAny<ImportFile>()), Times.Never());
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Files", _file, It.IsAny<string>(), 1), Times.Once());
        }

        [Test]
        public void park_is_idempotent()
        {
            Mocker.GetMock<IImportFileRepository>()
                  .Setup(s => s.FindByPath(_file))
                  .Returns(new ImportFile { Id = 1, RootFolderId = 3, Path = _file });

            Subject.Park(_file, 4, "Files", "No match").Should().BeTrue();

            Mocker.GetMock<IImportFileRepository>().Verify(s => s.Insert(It.IsAny<ImportFile>()), Times.Never());
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(It.IsAny<LegacyReportCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }
    }
}
