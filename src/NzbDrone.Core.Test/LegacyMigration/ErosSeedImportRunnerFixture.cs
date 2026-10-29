using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class ErosSeedImportRunnerFixture : CoreTest<ErosSeedImportRunner>
    {
        private LegacyMigrationState _state;
        private List<LegacyMovieSeed> _seeds;
        private LegacyMovieSeed _seed;
        private ImportFile _importFile;
        private Mock<ILegacyImportProgress> _progress;
        private string _path;

        [SetUp]
        public void Setup()
        {
            _path = "/data/library/Studio/movie.mp4".AsOsAgnostic();
            _state = new LegacyMigrationState { SourceKind = LegacyKind.Eros, MovieSource = MovieMetadataType.TMDB };
            _importFile = new ImportFile { Id = 5, Path = _path };
            _seed = new LegacyMovieSeed
            {
                Id = 1,
                LegacyForeignId = "legacy-1",
                Title = "The Big Movie",
                Year = 2020,
                TmdbId = 123,
                Monitored = true,
                QualityProfileId = 4,
                RootFolderPath = "/data/library".AsOsAgnostic(),
                Tags = "[2,3]",
                ImportFileId = 5
            };
            _seeds = new List<LegacyMovieSeed> { _seed };
            _progress = new Mock<ILegacyImportProgress>();

            Mocker.GetMock<ILegacyMovieSeedRepository>().Setup(r => r.All()).Returns(() => _seeds);
            Mocker.GetMock<IImportlFileService>().Setup(s => s.Get(5)).Returns(_importFile);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(_path)).Returns(1234);
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovie(It.IsAny<Movie>()))
                  .Returns<Movie>(m =>
                  {
                      m.Id = 77;
                      return m;
                  });
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>()))
                  .Returns(LinkOutcome.Linked);
        }

        private static Movie Candidate(string title, int year, int tmdbId = 123)
        {
            return new Movie
            {
                TmdbId = tmdbId,
                MovieMetadata = new MovieMetadata { Title = title, Year = year, TmdbId = tmdbId, ForeignId = "new-" + tmdbId }
            };
        }

        private void GivenSearch(string term, params Movie[] results)
        {
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie(term))
                  .Returns(new List<Movie>(results));
        }

        private void GivenTitleSearch(params Movie[] results)
        {
            _state.MovieSource = MovieMetadataType.TPDB;
            GivenSearch("The Big Movie 2020", results);
        }

        private LegacyImportPass Run()
        {
            return Subject.RunPass(_state, _progress.Object);
        }

        private void VerifyNeedsAttention(Times times)
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Movies", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), times);
        }

        [Test]
        public void handles_only_eros()
        {
            Subject.Handles(_state).Should().BeTrue();
            _state.SourceKind = LegacyKind.V2;
            Subject.Handles(_state).Should().BeFalse();
        }

        [Test]
        public void tmdb_seed_added_and_file_linked_and_import_file_removed()
        {
            GivenSearch("tmdb:123", Candidate("Other", 1999, 999), Candidate("The Big Movie", 2020));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovie(It.Is<Movie>(m => m.Monitored && m.QualityProfileId == 4 && m.RootFolderPath == _seed.RootFolderPath && m.Tags.SetEquals(new[] { 2, 3 }) && !m.AddOptions.SearchForMovie)), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.Id == 77), _path, It.IsAny<Action<MovieFile>>()), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved)), Times.Once());
            _progress.Verify(p => p.Advance("Movies", 1), Times.Once());
        }

        [Test]
        public void fill_sets_size_and_date_added()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Action<MovieFile> fill = null;
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>()))
                  .Callback<Movie, string, Action<MovieFile>>((m, p, f) => fill = f)
                  .Returns(LinkOutcome.Linked);

            Run();

            var file = new MovieFile();
            fill(file);
            file.Size.Should().Be(1234);
            file.DateAdded.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void title_search_single_exact_match_links()
        {
            GivenTitleSearch(Candidate("The Big Movie!", 2020, 0));

            Run();

            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>()), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
            VerifyNeedsAttention(Times.Never());
        }

        [Test]
        public void title_search_two_results_needs_attention_keeps_import_file()
        {
            GivenTitleSearch(Candidate("The Big Movie", 2020, 1), Candidate("The Big Movie", 2020, 2));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Movies", "The Big Movie (2020)", "legacy-1", 1), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Never());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved)), Times.Once());
        }

        [Test]
        public void year_mismatch_needs_attention()
        {
            GivenTitleSearch(Candidate("The Big Movie", 2019));

            Run();

            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void existing_movie_reused_not_added_twice()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByForeignId("new-123"))
                  .Returns(new Movie { Id = 50 });

            Run();

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Never());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.Id == 50), _path, It.IsAny<Action<MovieFile>>()), Times.Once());
        }

        [Test]
        public void other_link_outcome_keeps_import_file_and_reports()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>()))
                  .Returns(LinkOutcome.Missing);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void transient_error_records_attempt_and_stays_pending()
        {
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException());
            _seed.Attempts = 1;

            var pass = Run();

            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.NextAttempt.Should().Be(2);
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Attempts == 2 && s.LastAttemptAt != null && !s.Resolved)), Times.Once());
            _progress.Verify(p => p.Advance(It.IsAny<string>(), It.IsAny<long>()), Times.Never());
        }

        [Test]
        public void fifth_failure_gives_up_as_needs_attention()
        {
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException());
            _seed.Attempts = 4;

            var pass = Run();

            pass.Pending.Should().Be(0);
            pass.WaitingRetry.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Attempts == 5 && s.Resolved)), Times.Once());
            _progress.Verify(p => p.Advance("Movies", 1), Times.Once());
        }

        [Test]
        public void resolved_seeds_are_skipped_on_rerun()
        {
            _seed.Resolved = true;

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ISearchForNewMovie>().Verify(s => s.SearchForNewMovie(It.IsAny<string>()), Times.Never());
            _progress.Verify(p => p.Total(1), Times.Once());
        }

        [Test]
        public void non_transient_add_failure_resolves_as_needs_attention()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovie(It.IsAny<Movie>()))
                  .Throws(new FluentValidation.ValidationException("Path conflict"));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Movies", "The Big Movie (2020)", It.Is<string>(d => d.Contains("Path conflict")), 1), Times.Once());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved && s.Attempts == 0)), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void malformed_tags_resolve_as_needs_attention()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            _seed.Tags = "not json";

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved)), Times.Once());
        }

        [Test]
        public void missing_import_file_resolves_as_needs_attention()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<IImportlFileService>().Setup(s => s.Get(5)).Returns((ImportFile)null);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>()), Times.Never());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved)), Times.Once());
        }

        [Test]
        public void added_movie_never_searches()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));

            Run();

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovie(It.Is<Movie>(m => m.AddOptions != null && !m.AddOptions.SearchForMovie)), Times.Once());
        }
    }
}
