using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.LegacyMigration.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacyMigrationServiceFixture : DbTest<LegacyMigrationService, LegacyMigrationState>
    {
        private LegacyMigrationStateRepository _stateRepository;
        private LegacyMigrationReportRepository _reportRepository;

        [SetUp]
        public void Setup()
        {
            // The service wraps the real repositories, so its reads and writes go through the test database
            _stateRepository = Mocker.Resolve<LegacyMigrationStateRepository>();
            _reportRepository = Mocker.Resolve<LegacyMigrationReportRepository>();

            Mocker.SetConstant<ILegacyMigrationStateRepository>(_stateRepository);
            Mocker.SetConstant<ILegacyMigrationReportRepository>(_reportRepository);
        }

        private LegacyMigrationState GivenState()
        {
            return _stateRepository.Insert(new LegacyMigrationState
            {
                SourceKind = LegacyKind.Eros,
                SceneSource = SceneMetadataType.Tpdb,
                MovieSource = MovieMetadataType.TMDB,
                Phase = "Phase2Pending"
            });
        }

        [Test]
        public void is_incomplete_false_without_state_row()
        {
            Subject.GetCurrent().Should().BeNull();
            Subject.IsIncomplete().Should().BeFalse();
        }

        [Test]
        public void is_incomplete_true_until_mark_completed()
        {
            GivenState();

            Subject.IsIncomplete().Should().BeTrue();

            Subject.MarkCompleted();

            Subject.IsIncomplete().Should().BeFalse();

            var state = Subject.GetCurrent();

            state.Completed.Should().BeTrue();
            state.CompletedAt.Should().NotBeNull();
            state.Phase.Should().Be("Done");

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<LegacyMigrationCompletedEvent>()), Times.Once());
        }

        [Test]
        public void save_progress_inserts_then_updates()
        {
            var state = new LegacyMigrationState
            {
                SourceKind = LegacyKind.V2,
                SceneSource = SceneMetadataType.Tpdb,
                MovieSource = MovieMetadataType.TMDB,
                Phase = "Series"
            };

            Subject.IsIncomplete().Should().BeFalse();

            Subject.SaveProgress(state);

            Subject.IsIncomplete().Should().BeTrue();

            state.Phase = "Episodes";
            Subject.SaveProgress(state);

            _stateRepository.All().Should().ContainSingle().Which.Phase.Should().Be("Episodes");
        }

        [Test]
        public void dismiss_report_persists()
        {
            GivenState();

            Subject.DismissReport();

            Subject.GetCurrent().ReportDismissed.Should().BeTrue();
        }

        [Test]
        public void dismiss_report_publishes_event()
        {
            GivenState();

            Subject.DismissReport();

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<LegacyMigrationReportDismissedEvent>()), Times.Once());
        }

        [Test]
        public void report_appends_item_with_count_default_1()
        {
            Subject.Report(LegacyReportCategory.NeedsAttention, "Series", "Studio", "No match");

            var report = Subject.GetReport();

            report.Should().ContainSingle();
            report[0].Category.Should().Be(LegacyReportCategory.NeedsAttention);
            report[0].Area.Should().Be("Series");
            report[0].Subject.Should().Be("Studio");
            report[0].Detail.Should().Be("No match");
            report[0].Count.Should().Be(1);
        }

        [Test]
        public void adding_a_file_clears_the_report_rows_about_it()
        {
            const string path = "/media/movies/A Scene (2011)/A Scene (2011).mp4";

            Subject.Report(LegacyReportCategory.NeedsAttention, "Movies", path, "File could not be linked: Missing");
            Subject.Report(LegacyReportCategory.UnmatchedFile, "Scenes", path, "No match");
            Subject.Report(LegacyReportCategory.NeedsAttention, "Movies", "/media/movies/Other.mp4", "File could not be linked: Missing");

            Subject.Handle(new MovieFileAddedEvent(new MovieFile { MovieId = 1, Path = path }));

            Subject.GetReport().Should().ContainSingle()
                   .Which.Subject.Should().Be("/media/movies/Other.mp4");
        }

        [Test]
        public void adding_an_unmapped_file_keeps_the_report_rows_about_it()
        {
            const string path = "/media/movies/A Scene (2011)/A Scene (2011).mp4";

            Subject.Report(LegacyReportCategory.UnmatchedFile, "Scenes", path, "No match");

            Subject.Handle(new MovieFileAddedEvent(new MovieFile { MovieId = 0, Path = path }));

            Subject.GetReport().Should().ContainSingle();
        }

        [Test]
        public void adding_a_file_without_a_path_keeps_the_report()
        {
            Subject.Report(LegacyReportCategory.SkippedSetting, "Config", null, "Skipped");

            Subject.Handle(new MovieFileAddedEvent(new MovieFile { MovieId = 1 }));

            Subject.GetReport().Should().ContainSingle();
        }

        [Test]
        public void migration_307_defaults()
        {
            var series = Mocker.Resolve<LegacyV2SeriesRepository>().Insert(new LegacyV2Series
            {
                LegacyId = 1,
                Title = "Studio",
                Path = "/media/studio",
                Tags = "[]"
            });

            var seed = Mocker.Resolve<LegacyMovieSeedRepository>().Insert(new LegacyMovieSeed
            {
                LegacyForeignId = "1",
                Title = "Movie"
            });

            var storedSeries = Mocker.Resolve<LegacyV2SeriesRepository>().Get(series.Id);
            var storedSeed = Mocker.Resolve<LegacyMovieSeedRepository>().Get(seed.Id);

            storedSeries.Attempts.Should().Be(0);
            storedSeries.LastAttemptAt.Should().BeNull();
            storedSeed.Attempts.Should().Be(0);
            storedSeed.LastAttemptAt.Should().BeNull();

            GivenState().ReportDismissed.Should().BeFalse();
            Subject.GetCurrent().ReportDismissed.Should().BeFalse();
        }
    }
}
