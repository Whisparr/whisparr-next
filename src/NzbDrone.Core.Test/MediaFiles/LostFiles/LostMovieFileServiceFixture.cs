using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.LostFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.LostFiles
{
    [TestFixture]
    public class LostMovieFileServiceFixture : CoreTest<LostMovieFileService>
    {
        private const string Hash = "aaaaaaaaaaaaaaaa";
        private const long Size = 1234;

        private Movie _movie;
        private string _newPath;
        private LostMovieFile _entry;
        private List<MovieFile> _movieFiles;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 7, Path = @"C:\lib\Old Studio".AsOsAgnostic() };
            _newPath = @"C:\lib\New Studio\scene.mkv".AsOsAgnostic();
            _entry = new LostMovieFile { Id = 1, MovieId = 7, OsHash = Hash, Size = Size, RelativePath = "scene.mkv", LostAt = DateTime.UtcNow.AddDays(-1) };
            _movieFiles = new List<MovieFile>();

            Mocker.GetMock<ILostMovieFileRepository>().Setup(s => s.FindByHash(Hash, Size)).Returns(new List<LostMovieFile> { _entry });
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(7)).Returns(_movie);
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(It.IsAny<string>(), It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(7)).Returns(() => _movieFiles);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), null)).Returns(@"C:\lib".AsOsAgnostic());
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), false))
                  .Returns<List<string>, Movie, bool>((paths, movie, _) => new List<ImportDecision> { new(new Parser.Model.LocalMovie { Movie = movie, Path = paths[0] }) });
            Mocker.GetMock<IImportApprovedMovie>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), false, null, ImportMode.Auto))
                  .Returns<List<ImportDecision>, bool, Core.Download.DownloadClientItem, ImportMode>((decisions, _, _, _) =>
                  {
                      if (decisions.Count == 0)
                      {
                          return new List<ImportResult>();
                      }

                      _movieFiles = new List<MovieFile> { new() { MovieId = 7, RelativePath = decisions[0].LocalMovie.Movie.Path.GetRelativePath(decisions[0].LocalMovie.Path) } };

                      return decisions.Select(d => new ImportResult(d)).ToList();
                  });
        }

        [Test]
        public void record_skips_files_without_oshash()
        {
            Subject.Record(_movie, new MovieFile { RelativePath = "scene.mkv", Size = Size, OsHash = null });

            Mocker.GetMock<ILostMovieFileRepository>().Verify(v => v.Insert(It.IsAny<LostMovieFile>()), Times.Never());
        }

        [Test]
        public void record_stores_hash_size_and_old_path()
        {
            Subject.Record(_movie, new MovieFile { RelativePath = "scene.mkv", Size = Size, OsHash = Hash });

            Mocker.GetMock<ILostMovieFileRepository>()
                  .Verify(v => v.Insert(It.Is<LostMovieFile>(e => e.MovieId == 7 && e.OsHash == Hash && e.Size == Size && e.RelativePath == "scene.mkv" && e.LostAt > DateTime.UtcNow.AddMinutes(-1))), Times.Once());
        }

        [Test]
        public void relinks_single_match_in_place_and_sets_movie_path_to_file_folder()
        {
            var file = Subject.TryRelink(_newPath, Hash, Size);

            file.Should().NotBeNull();
            file.RelativePath.Should().Be("scene.mkv");
            _movie.Path.Should().Be(Path.GetDirectoryName(_newPath));

            // Nothing is moved or copied: the import runs as an existing-file scan, not a new download
            Mocker.GetMock<IImportApprovedMovie>().Verify(v => v.Import(It.IsAny<List<ImportDecision>>(), false, null, ImportMode.Auto), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(v => v.UpdateMovie(_movie), Times.Once());
            Mocker.GetMock<ILostMovieFileRepository>().Verify(v => v.Delete(_entry), Times.Once());
        }

        [Test]
        public void does_not_relink_when_two_entries_share_hash_and_size()
        {
            var other = new LostMovieFile { Id = 2, MovieId = 8, OsHash = Hash, Size = Size, RelativePath = "copy.mkv", LostAt = DateTime.UtcNow.AddDays(-2) };
            Mocker.GetMock<ILostMovieFileRepository>().Setup(s => s.FindByHash(Hash, Size)).Returns(new List<LostMovieFile> { _entry, other });

            Subject.TryRelink(_newPath, Hash, Size).Should().BeNull();

            VerifyNothingRelinked();
        }

        [Test]
        public void does_not_relink_after_thirty_days()
        {
            _entry.LostAt = DateTime.UtcNow.AddDays(-31);

            Subject.TryRelink(_newPath, Hash, Size).Should().BeNull();

            VerifyNothingRelinked();
        }

        [Test]
        public void does_not_relink_when_scene_already_has_a_file()
        {
            _movieFiles = new List<MovieFile> { new() { MovieId = 7, RelativePath = "other.mkv" } };

            Subject.TryRelink(_newPath, Hash, Size).Should().BeNull();

            VerifyNothingRelinked();
        }

        [Test]
        public void does_not_relink_without_a_matching_entry()
        {
            Mocker.GetMock<ILostMovieFileRepository>().Setup(s => s.FindByHash("bbbbbbbbbbbbbbbb", Size)).Returns(new List<LostMovieFile>());

            Subject.TryRelink(_newPath, "bbbbbbbbbbbbbbbb", Size).Should().BeNull();

            VerifyNothingRelinked();
        }

        [Test]
        public void does_not_relink_across_root_folders()
        {
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolderPath(_movie.Path, null)).Returns(@"C:\lib".AsOsAgnostic());
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolderPath(_newPath, null)).Returns(@"D:\other".AsOsAgnostic());

            Subject.TryRelink(_newPath, Hash, Size).Should().BeNull();

            VerifyNothingRelinked();
        }

        [Test]
        public void does_not_relink_file_tracked_by_another_item()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(It.IsAny<string>(), It.IsAny<List<string>>()))
                  .Returns(new List<string>());

            Subject.TryRelink(_newPath, Hash, Size).Should().BeNull();

            VerifyNothingRelinked();
            Mocker.GetMock<IMovieService>().Verify(v => v.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void saves_new_movie_path_before_the_import_runs()
        {
            var savedPaths = new List<string>();
            string pathDuringImport = null;

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.UpdateMovie(It.IsAny<Movie>()))
                  .Callback<Movie>(m => savedPaths.Add(m.Path));
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), false))
                  .Callback(() => pathDuringImport = savedPaths.LastOrDefault())
                  .Returns(new List<ImportDecision>());

            Subject.TryRelink(_newPath, Hash, Size);

            pathDuringImport.Should().Be(Path.GetDirectoryName(_newPath));
        }

        [Test]
        public void restores_and_saves_old_path_when_the_import_throws()
        {
            var previousPath = _movie.Path;
            var savedPaths = new List<string>();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.UpdateMovie(It.IsAny<Movie>()))
                  .Callback<Movie>(m => savedPaths.Add(m.Path));
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), false))
                  .Throws(new InvalidOperationException());

            Action act = () => Subject.TryRelink(_newPath, Hash, Size);

            act.Should().Throw<InvalidOperationException>();
            _movie.Path.Should().Be(previousPath);
            savedPaths[^1].Should().Be(previousPath);
        }

        [Test]
        public void keeps_entry_and_movie_path_when_import_does_not_approve_the_file()
        {
            var previousPath = _movie.Path;
            var savedPaths = new List<string>();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.UpdateMovie(It.IsAny<Movie>()))
                  .Callback<Movie>(m => savedPaths.Add(m.Path));

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), false))
                  .Returns(new List<ImportDecision>());
            Mocker.GetMock<IImportApprovedMovie>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), false, null, ImportMode.Auto))
                  .Returns(new List<ImportResult>());

            Subject.TryRelink(_newPath, Hash, Size).Should().BeNull();

            _movie.Path.Should().Be(previousPath);

            // Saved with the new path for the import, then saved again with the old one
            savedPaths.Should().Equal(Path.GetDirectoryName(_newPath), previousPath);
            Mocker.GetMock<ILostMovieFileRepository>().Verify(v => v.Delete(It.IsAny<LostMovieFile>()), Times.Never());
        }

        [Test]
        public void entries_removed_when_movie_deleted()
        {
            Subject.Handle(new MoviesDeletedEvent(new List<Movie> { _movie }, false, false));

            Mocker.GetMock<ILostMovieFileRepository>().Verify(v => v.DeleteForMovies(It.Is<List<int>>(l => l.SequenceEqual(new[] { 7 }))), Times.Once());
        }

        [Test]
        public void entries_removed_when_movie_gets_a_file()
        {
            Subject.Handle(new MovieFileImportedEvent(null, new MovieFile { MovieId = 7 }, new List<DeletedMovieFile>(), false, null));

            Mocker.GetMock<ILostMovieFileRepository>().Verify(v => v.DeleteForMovies(It.Is<List<int>>(l => l.SequenceEqual(new[] { 7 }))), Times.Once());
        }

        [Test]
        public void any_reflects_the_entry_count()
        {
            Mocker.GetMock<ILostMovieFileRepository>().SetupSequence(s => s.Count()).Returns(0).Returns(2);

            Subject.Any().Should().BeFalse();
            Subject.Any().Should().BeTrue();
        }

        private void VerifyNothingRelinked()
        {
            Mocker.GetMock<IImportApprovedMovie>().Verify(v => v.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), null, ImportMode.Auto), Times.Never());
            Mocker.GetMock<ILostMovieFileRepository>().Verify(v => v.Delete(It.IsAny<LostMovieFile>()), Times.Never());
        }
    }
}
