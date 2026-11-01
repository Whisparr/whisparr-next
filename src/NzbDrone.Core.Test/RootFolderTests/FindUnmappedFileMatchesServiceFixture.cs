using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.RootFolders.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class FindUnmappedFileMatchesServiceFixture : CoreTest<FindUnmappedFileMatchesService>
    {
        private Dictionary<int, ImportFile> _files;
        private List<ImportFile> _saved;

        [SetUp]
        public void Setup()
        {
            _files = new Dictionary<int, ImportFile>();
            _saved = new List<ImportFile>();

            Mocker.GetMock<IImportFileRepository>()
                .Setup(r => r.FindByIds(It.IsAny<IEnumerable<int>>()))
                .Returns((IEnumerable<int> ids) => ids.Where(_files.ContainsKey).Select(i => _files[i]).ToList());

            Mocker.GetMock<IImportFileRepository>()
                .Setup(r => r.SaveTopMatch(It.IsAny<IList<ImportFile>>()))
                .Callback((IList<ImportFile> files) => _saved.AddRange(files.Select(Clone)));

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(true);
        }

        private static ImportFile Clone(ImportFile f)
        {
            return new ImportFile { Id = f.Id, TopMatch = f.TopMatch, TopMatchCheckedAt = f.TopMatchCheckedAt };
        }

        private void GivenFile(int id, bool dismissed = false, UnmappedFileMatch previous = null, DateTime? previousCheckedAt = null)
        {
            var file = new ImportFile
            {
                Id = id,
                Path = $"/media/file{id}.mp4",
                Dismissed = dismissed,
                TopMatch = previous,
                TopMatchCheckedAt = previousCheckedAt
            };

            _files[id] = file;
        }

        private static UnmappedFileSuggestion Suggestion(string foreignId, string title = "Title", bool sameDate = true, bool sameStudio = true, bool hash = false)
        {
            var movie = new Movie();

            movie.MovieMetadata.Value.ForeignId = foreignId;
            movie.MovieMetadata.Value.Title = title;
            movie.MovieMetadata.Value.StudioTitle = "Studio";
            movie.MovieMetadata.Value.ReleaseDateUtc = new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Utc);

            return new UnmappedFileSuggestion { Movie = movie, SameDate = sameDate, SameStudio = sameStudio, HashMatch = hash };
        }

        private void GivenSuggestions(int id, params UnmappedFileSuggestion[] results)
        {
            Mocker.GetMock<IUnmappedFileService>()
                .Setup(s => s.Suggest(id, null, ItemType.Scene))
                .Returns(new UnmappedFileSuggestions { Term = "t", Results = results.ToList() });
        }

        private void GivenSearchFails(int id)
        {
            Mocker.GetMock<IUnmappedFileService>()
                .Setup(s => s.Suggest(id, null, ItemType.Scene))
                .Throws(new InvalidOperationException("search down"));
        }

        private static FindUnmappedFileMatchesCommand Command(params int[] ids)
        {
            return new FindUnmappedFileMatchesCommand { FileIds = ids.ToList() };
        }

        [Test]
        public void should_store_the_top_result_and_the_time_checked()
        {
            GivenFile(1);
            GivenSuggestions(1, Suggestion("top", "Top Scene", hash: true), Suggestion("second"));

            Subject.Execute(Command(1));

            var saved = _saved.Should().ContainSingle().Subject;
            saved.TopMatch.ForeignId.Should().Be("top");
            saved.TopMatch.Title.Should().Be("Top Scene");
            saved.TopMatch.StudioTitle.Should().Be("Studio");
            saved.TopMatch.ReleaseDate.Should().Be(new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Utc));
            saved.TopMatch.HashMatch.Should().BeTrue();
            saved.TopMatch.SameDate.Should().BeTrue();
            saved.TopMatch.SameStudio.Should().BeTrue();
            saved.TopMatchCheckedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Test]
        public void should_store_null_and_the_time_checked_when_there_are_no_results()
        {
            GivenFile(1, previous: new UnmappedFileMatch { ForeignId = "old" });
            GivenSuggestions(1);

            Subject.Execute(Command(1));

            var saved = _saved.Should().ContainSingle().Subject;
            saved.TopMatch.Should().BeNull();
            saved.TopMatchCheckedAt.Should().NotBeNull();
        }

        [Test]
        public void should_skip_missing_dismissed_and_off_disk_files_without_searching()
        {
            GivenFile(2, dismissed: true);
            GivenFile(3);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists("/media/file3.mp4")).Returns(false);

            Subject.Execute(Command(1, 2, 3));

            Mocker.GetMock<IUnmappedFileService>().Verify(s => s.Suggest(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<ItemType>()), Times.Never);
            Mocker.GetMock<IImportFileRepository>().Verify(r => r.SaveTopMatch(It.IsAny<IList<ImportFile>>()), Times.Never);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<UnmappedFileMatchCheckedEvent>()), Times.Never);
        }

        [Test]
        public void should_keep_the_previous_result_and_carry_on_when_a_search_fails()
        {
            var checkedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var previous = new UnmappedFileMatch { ForeignId = "old" };

            GivenFile(1, previous: previous, previousCheckedAt: checkedAt);
            GivenFile(2);
            GivenFile(3);
            GivenSearchFails(1);
            GivenSuggestions(2, Suggestion("two"));
            GivenSuggestions(3, Suggestion("three"));

            var command = Command(1, 2, 3);

            Subject.Execute(command);

            _saved.Select(f => f.Id).Should().Equal(2, 3);
            _files[1].TopMatch.Should().BeSameAs(previous);
            _files[1].TopMatchCheckedAt.Should().Be(checkedAt);
            Mocker.GetMock<ILocalizationService>().Verify(
                l => l.GetLocalizedString("FindUnmappedFileMatchesComplete", It.Is<Dictionary<string, object>>(t => (int)t["checked"] == 2 && (int)t["matched"] == 2 && (int)t["failed"] == 1)),
                Times.Once);
        }

        [Test]
        public void should_count_only_strong_matches_as_matched()
        {
            GivenFile(1);
            GivenFile(2);
            GivenSuggestions(1, Suggestion("one"));
            GivenSuggestions(2, Suggestion("two", sameStudio: false));

            Subject.Execute(Command(1, 2));

            Mocker.GetMock<ILocalizationService>().Verify(
                l => l.GetLocalizedString("FindUnmappedFileMatchesComplete", It.Is<Dictionary<string, object>>(t => (int)t["checked"] == 2 && (int)t["matched"] == 1 && (int)t["failed"] == 0)),
                Times.Once);
        }

        [Test]
        public void should_set_the_completion_message()
        {
            Mocker.GetMock<ILocalizationService>()
                .Setup(l => l.GetLocalizedString("FindUnmappedFileMatchesComplete", It.IsAny<Dictionary<string, object>>()))
                .Returns("done");
            GivenFile(1);
            GivenSuggestions(1);

            var command = Command(1);

            Subject.Execute(command);

            command.CompletionMessage.Should().Be("done");
        }

        [Test]
        public void should_stop_and_fail_when_the_first_three_files_all_fail()
        {
            foreach (var id in new[] { 1, 2, 3, 4 })
            {
                GivenFile(id);
                GivenSearchFails(id);
            }

            var action = () => Subject.Execute(Command(1, 2, 3, 4));

            action.Should().Throw<CommandFailedException>();
            Mocker.GetMock<IUnmappedFileService>().Verify(s => s.Suggest(4, null, ItemType.Scene), Times.Never);
            _saved.Should().BeEmpty();
        }

        [Test]
        public void should_not_stop_when_a_success_comes_before_three_failures()
        {
            GivenFile(1);
            GivenSuggestions(1, Suggestion("one"));

            foreach (var id in new[] { 2, 3, 4 })
            {
                GivenFile(id);
                GivenSearchFails(id);
            }

            var action = () => Subject.Execute(Command(1, 2, 3, 4));

            action.Should().NotThrow();
            _saved.Should().ContainSingle();
        }

        [Test]
        public void should_report_progress_as_done_of_total()
        {
            GivenFile(1);
            GivenFile(2, dismissed: true);
            GivenFile(3);
            GivenSuggestions(1, Suggestion("one"));
            GivenSuggestions(3, Suggestion("three"));

            Subject.Execute(Command(1, 2, 3));

            var progress = Mocker.GetMock<IReportCommandProgress>();
            progress.Verify(p => p.Report(It.IsAny<string>(), 1, 3, It.IsAny<double>()), Times.Once);
            progress.Verify(p => p.Report(It.IsAny<string>(), 2, 3, It.IsAny<double>()), Times.Once);
            progress.Verify(p => p.Report(It.IsAny<string>(), 3, 3, 100), Times.Once);
        }

        [Test]
        public void should_publish_an_event_for_each_file_stored()
        {
            GivenFile(1);
            GivenFile(2);
            GivenFile(3);
            GivenSuggestions(1, Suggestion("one"));
            GivenSuggestions(2);
            GivenSearchFails(3);

            Subject.Execute(Command(1, 2, 3));

            var events = Mocker.GetMock<IEventAggregator>();
            events.Verify(e => e.PublishEvent(It.Is<UnmappedFileMatchCheckedEvent>(m => m.File.Id == 1 && m.File.TopMatch.ForeignId == "one")), Times.Once);
            events.Verify(e => e.PublishEvent(It.Is<UnmappedFileMatchCheckedEvent>(m => m.File.Id == 2 && m.File.TopMatch == null)), Times.Once);
            events.Verify(e => e.PublishEvent(It.IsAny<UnmappedFileMatchCheckedEvent>()), Times.Exactly(2));
        }

        [Test]
        public void should_skip_an_id_that_is_no_longer_stored_and_search_the_rest()
        {
            GivenFile(1);
            GivenFile(3);
            GivenSuggestions(1, Suggestion("one"));
            GivenSuggestions(3, Suggestion("three"));

            Subject.Execute(Command(1, 2, 3));

            _saved.Select(f => f.Id).Should().Equal(1, 3);
            Mocker.GetMock<IUnmappedFileService>().Verify(s => s.Suggest(2, null, ItemType.Scene), Times.Never);
        }

        [Test]
        public void should_treat_a_row_that_vanishes_before_its_search_as_a_skip_not_a_failure()
        {
            foreach (var id in new[] { 1, 2, 3, 4 })
            {
                GivenFile(id);
            }

            for (var id = 1; id <= 3; id++)
            {
                Mocker.GetMock<IUnmappedFileService>()
                    .Setup(s => s.Suggest(id, null, ItemType.Scene))
                    .Throws(new ModelNotFoundException(typeof(ImportFile), id));
            }

            GivenSuggestions(4, Suggestion("four"));

            var action = () => Subject.Execute(Command(1, 2, 3, 4));

            action.Should().NotThrow();
            _saved.Select(f => f.Id).Should().Equal(4);
            Mocker.GetMock<ILocalizationService>().Verify(
                l => l.GetLocalizedString("FindUnmappedFileMatchesComplete", It.Is<Dictionary<string, object>>(t => (int)t["checked"] == 1 && (int)t["failed"] == 0)),
                Times.Once);
        }

        [Test]
        public void should_do_nothing_without_ids()
        {
            Subject.Execute(new FindUnmappedFileMatchesCommand());

            Mocker.GetMock<IUnmappedFileService>().VerifyNoOtherCalls();
        }
    }
}
