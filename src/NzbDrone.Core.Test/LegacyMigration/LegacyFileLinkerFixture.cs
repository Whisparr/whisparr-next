using System.Collections.Generic;
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

        [Test]
        public void linked_to_other_movie_is_left_alone()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FindByFullPath(_file))
                  .Returns(new MovieFile { Id = 5, MovieId = 8, Path = _file, RelativePath = "scene.mp4" });

            Subject.Link(_movie, _file, f => { }).Should().Be(LinkOutcome.LinkedElsewhere);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Add(It.IsAny<MovieFile>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
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
