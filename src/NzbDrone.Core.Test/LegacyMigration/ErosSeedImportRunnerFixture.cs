using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
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
        private DateTime _now;

        [SetUp]
        public void Setup()
        {
            _path = @"C:\data\library\Studio\movie.mp4".AsOsAgnostic();
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
                RootFolderPath = @"C:\data\library".AsOsAgnostic(),
                Tags = "[2,3]",
                ImportFileId = 5
            };
            _seeds = new List<LegacyMovieSeed> { _seed };
            _progress = new Mock<ILegacyImportProgress>();
            _now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

            Mocker.GetMock<ISkyhookPacer>().SetupGet(p => p.UtcNow).Returns(() => _now);

            Mocker.GetMock<ILegacyMovieSeedRepository>().Setup(r => r.All()).Returns(() => _seeds);
            Mocker.GetMock<IImportlFileService>().Setup(s => s.Get(5)).Returns(_importFile);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(_path)).Returns(1234);
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns<List<Movie>, bool, bool>((movies, _, _) =>
                  {
                      movies.ForEach(m => m.Id = 77);
                      return movies;
                  });
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
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

        private LegacyImportPass Run(bool recordOutages = false)
        {
            return Subject.RunPass(_state, _progress.Object, recordOutages);
        }

        private void VerifyNeverAdded()
        {
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
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
        public void does_not_handle_rebuild()
        {
            _state.Rebuild = true;

            Subject.Handles(_state).Should().BeFalse();
        }

        [Test]
        public void tmdb_seed_added_and_file_linked_and_import_file_removed()
        {
            GivenSearch("tmdb:123", Candidate("Other", 1999, 999), Candidate("The Big Movie", 2020));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovies(It.Is<List<Movie>>(l => l.Count == 1 && l[0].Monitored && l[0].QualityProfileId == 4 && l[0].RootFolderPath == _seed.RootFolderPath && l[0].Tags.SetEquals(new[] { 2, 3 }) && !l[0].AddOptions.SearchForMovie), false, true), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.Id == 77), _path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Once());
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
                  .Setup(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Callback<Movie, string, Action<MovieFile>, MatchTier>((m, p, f, t) => fill = f)
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

            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(5), Times.Once());
            VerifyNeedsAttention(Times.Never());
        }

        [Test]
        public void title_search_two_results_needs_attention_keeps_import_file()
        {
            GivenTitleSearch(Candidate("The Big Movie", 2020, 1), Candidate("The Big Movie", 2020, 2));

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Movies", "The Big Movie (2020)", "Legacy TMDB id legacy-1", 1), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
            VerifyNeverAdded();
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved)), Times.Once());
        }

        [Test]
        public void unmatched_seed_names_the_other_source_when_tmdb_is_chosen()
        {
            _state.MovieSource = MovieMetadataType.TMDB;
            _seed.TmdbId = 0;
            GivenSearch("The Big Movie 2020");

            Run();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Movies", "The Big Movie (2020)", "Legacy TPDB id legacy-1", 1), Times.Once());
        }

        [Test]
        public void give_up_detail_names_the_legacy_id_source()
        {
            AddHealthySeed();
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException("slow"));
            _seed.Attempts = 4;

            Run();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Movies", "The Big Movie (2020)", "Legacy TPDB id legacy-1: slow", 1), Times.Once());
        }

        [Test]
        public void year_mismatch_needs_attention()
        {
            GivenTitleSearch(Candidate("The Big Movie", 2019));

            Run();

            VerifyNeedsAttention(Times.Once());
            VerifyNeverAdded();
        }

        [Test]
        public void existing_movie_reused_not_added_twice()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByForeignId("new-123"))
                  .Returns(new Movie { Id = 50 });

            Run();

            VerifyNeverAdded();
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.Is<Movie>(m => m.Id == 50), _path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Once());
        }

        [Test]
        public void other_link_outcome_keeps_import_file_and_reports()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns(LinkOutcome.Missing);

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void scene_that_already_has_a_file_reports_and_keeps_the_import_file()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<ILegacyFileLinker>()
                  .Setup(l => l.Link(It.IsAny<Movie>(), _path, It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()))
                  .Returns(LinkOutcome.SceneHasOtherFile);

            var pass = Run();

            pass.Pending.Should().Be(0);
            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Movies", _path, It.Is<string>(d => d.StartsWith("Scene already has a file") && d.Contains(_path)), 1), Times.Once());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.Remove(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void transient_error_records_attempt_and_stays_pending()
        {
            AddHealthySeed();
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException());
            _seed.Attempts = 1;

            var pass = Run();

            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.Outage.Should().BeFalse();
            pass.NextDue.Should().Be(_now.AddMinutes(30));
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Attempts == 2 && s.LastAttemptAt == _now && !s.Resolved)), Times.Once());
            _progress.Verify(p => p.Advance(It.IsAny<string>(), It.IsAny<long>()), Times.Once());
        }

        [Test]
        public void fifth_failure_gives_up_as_needs_attention()
        {
            AddHealthySeed();
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException());
            _seed.Attempts = 4;

            var pass = Run();

            pass.Pending.Should().Be(0);
            pass.WaitingRetry.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Attempts == 5 && s.Resolved)), Times.Once());
            _progress.Verify(p => p.Advance("Movies", 1), Times.Exactly(2));
        }

        [Test]
        public void every_seed_failing_transiently_is_an_outage_that_spends_no_attempts()
        {
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException());

            var pass = Run();

            pass.Outage.Should().BeTrue();
            pass.Attempted.Should().Be(1);
            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.NextDue.Should().BeNull();
            _seed.Attempts.Should().Be(0);
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.IsAny<LegacyMovieSeed>()), Times.Never());
        }

        [Test]
        public void outage_attempts_are_spent_when_the_pass_is_told_to_record_them()
        {
            Mocker.GetMock<ISearchForNewMovie>()
                  .Setup(s => s.SearchForNewMovie("tmdb:123"))
                  .Throws(new TimeoutException());

            var pass = Run(recordOutages: true);

            pass.Outage.Should().BeTrue();
            pass.NextDue.Should().Be(_now.AddMinutes(5));
            _seed.Attempts.Should().Be(1);
            _seed.LastAttemptAt.Should().Be(_now);
        }

        [Test]
        public void a_seed_still_backing_off_is_skipped()
        {
            _seed.Attempts = 1;
            _seed.LastAttemptAt = _now.AddMinutes(-4);

            var pass = Run();

            pass.Attempted.Should().Be(0);
            pass.Pending.Should().Be(1);
            pass.WaitingRetry.Should().Be(1);
            pass.NextDue.Should().Be(_now.AddMinutes(1));
            Mocker.GetMock<ISearchForNewMovie>().Verify(s => s.SearchForNewMovie(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_seed_whose_delay_has_passed_is_tried_again()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            _seed.Attempts = 1;
            _seed.LastAttemptAt = _now.AddMinutes(-5);

            var pass = Run();

            pass.Attempted.Should().Be(1);
            pass.Pending.Should().Be(0);
        }

        private void AddHealthySeed()
        {
            GivenSearch("tmdb:456", Candidate("Healthy", 2021, 456));
            _seeds.Add(new LegacyMovieSeed { Id = 2, LegacyForeignId = "legacy-2", Title = "Healthy", Year = 2021, TmdbId = 456, RootFolderPath = @"C:\data\library".AsOsAgnostic() });
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
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
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
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved)), Times.Once());
        }

        [Test]
        public void import_file_row_that_is_gone_is_treated_as_no_file()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<IImportlFileService>().Setup(s => s.Get(5)).Throws(new ModelNotFoundException(typeof(ImportFile), 5));

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<ILegacyFileLinker>().Verify(l => l.Link(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action<MovieFile>>(), It.IsAny<MatchTier>()), Times.Never());
            Mocker.GetMock<ILegacyMovieSeedRepository>().Verify(r => r.Update(It.Is<LegacyMovieSeed>(s => s.Resolved)), Times.Once());
        }

        [Test]
        public void added_movie_never_searches()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));

            Run();

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(l => l[0].AddOptions != null && !l[0].AddOptions.SearchForMovie), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());
        }

        [Test]
        public void added_movie_takes_the_parked_files_folder_as_its_path()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));

            Run();

            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovies(It.Is<List<Movie>>(l => l[0].Path == @"C:\data\library\Studio".AsOsAgnostic()), false, true), Times.Once());
        }

        [Test]
        public void file_directly_in_the_root_folder_leaves_the_path_to_the_naming_scheme()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            _importFile.Path = @"C:\data\library\movie.mp4".AsOsAgnostic();

            Run();

            Mocker.GetMock<IAddMovieService>()
                  .Verify(s => s.AddMovies(It.Is<List<Movie>>(l => string.IsNullOrEmpty(l[0].Path)), false, true), Times.Once());
        }

        [Test]
        public void add_that_returns_nothing_and_finds_nothing_needs_attention()
        {
            GivenSearch("tmdb:123", Candidate("The Big Movie", 2020));
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new List<Movie>());

            var pass = Run();

            pass.Pending.Should().Be(0);
            VerifyNeedsAttention(Times.Once());
        }
    }
}
