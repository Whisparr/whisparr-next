using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class UnmappedFileServiceFixture : CoreTest<UnmappedFileService>
    {
        private const string FilePath = "/media/Studio/Studio - 2024-03-05 - The Right Title [WEBDL-1080p].mp4";

        private ImportFile _file;

        [SetUp]
        public void Setup()
        {
            _file = new ImportFile { Id = 7, RootFolderId = 1, Path = FilePath, Name = "Studio - 2024-03-05 - The Right Title [WEBDL-1080p].mp4" };

            Mocker.GetMock<IImportFileRepository>().Setup(r => r.Get(7)).Returns(_file);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(FilePath)).Returns(true);
            Mocker.GetMock<ISearchForNewMovie>()
                .Setup(s => s.SearchForNewEntity(It.IsAny<string>(), It.IsAny<ItemType?>()))
                .Returns(new List<object>());
        }

        private static Movie Scene(string id, string title, string date = null, string studio = null)
        {
            var movie = new Movie();

            movie.MovieMetadata.Value.ForeignId = id;
            movie.MovieMetadata.Value.Title = title;
            movie.MovieMetadata.Value.ItemType = ItemType.Scene;
            movie.MovieMetadata.Value.StudioTitle = studio;
            movie.MovieMetadata.Value.ReleaseDateUtc = date == null ? null : DateTime.Parse(date + "T00:00:00Z").ToUniversalTime();

            return movie;
        }

        private void GivenSearch(string term, params Movie[] results)
        {
            Mocker.GetMock<ISearchForNewMovie>()
                .Setup(s => s.SearchForNewEntity(term, It.IsAny<ItemType?>()))
                .Returns(results.Cast<object>().ToList());
        }

        private void GivenLinkOutcome(LinkOutcome outcome)
        {
            Mocker.GetMock<ILegacyFileLinker>()
                .Setup(l => l.Link(It.IsAny<Movie>(), FilePath, It.IsAny<Action<MovieFile>>(), MatchTier.Manual))
                .Returns(outcome);
        }

        [Test]
        public void should_search_by_the_title_parsed_from_the_file_name()
        {
            Subject.Suggest(7, null, ItemType.Scene).Term.Should().Be("The Right Title");
        }

        [Test]
        public void should_prefer_the_term_the_user_typed()
        {
            Subject.Suggest(7, " something else ", ItemType.Scene).Term.Should().Be("something else");
        }

        [Test]
        public void should_rank_a_same_date_result_above_a_closer_title()
        {
            GivenSearch("The Right Title", Scene("a", "The Right Title", "2020-01-01"), Scene("b", "Other Name", "2024-03-05"));

            var results = Subject.Suggest(7, null, ItemType.Scene).Results;

            results.Select(r => r.Movie.ForeignId).Should().ContainInOrder("b", "a");
            results[0].SameDate.Should().BeTrue();
            results[1].TitleSimilarity.Should().Be(100);
        }

        [Test]
        public void should_rank_a_fingerprint_match_first_and_only_once()
        {
            _file.Candidates = new List<FingerprintCandidate> { new FingerprintCandidate { SceneId = "hash", LookupTerm = "stash:hash" } };

            GivenSearch("stash:hash", Scene("hash", "Different"));
            GivenSearch("The Right Title", Scene("a", "The Right Title", "2024-03-05"), Scene("hash", "Different"));

            var results = Subject.Suggest(7, null, ItemType.Scene).Results;

            results.Should().HaveCount(2);
            results[0].Movie.ForeignId.Should().Be("hash");
            results[0].HashMatch.Should().BeTrue();
        }

        [Test]
        public void should_never_link_while_suggesting()
        {
            GivenSearch("The Right Title", Scene("a", "The Right Title", "2024-03-05"));

            Subject.Suggest(7, null, ItemType.Scene);

            Mocker.GetMock<ILegacyFileLinker>().VerifyNoOtherCalls();
            Mocker.GetMock<IAddMovieService>().VerifyNoOtherCalls();
        }

        [TestCase("abc", "abc", 100)]
        [TestCase("abcd", "abzz", 50)]
        [TestCase("", "abc", 0)]
        public void should_score_title_similarity(string a, string b, int expected)
        {
            UnmappedFileService.GetTitleSimilarity(a, b).Should().Be(expected);
        }

        [Test]
        public void should_link_an_existing_scene_with_a_manual_tier_and_remove_the_row()
        {
            var movie = Scene("a", "The Right Title");

            Mocker.GetMock<IMovieService>().Setup(s => s.FindByForeignId("a")).Returns(movie);
            GivenLinkOutcome(LinkOutcome.Linked);

            Subject.Link(7, "a", ItemType.Scene, 1, true).Should().Be(movie);

            Mocker.GetMock<IImportFileRepository>().Verify(r => r.Delete(7), Times.Once);
            Mocker.GetMock<IAddMovieService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshNewScenes>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_add_a_new_scene_in_the_files_folder_then_refresh_it()
        {
            var movie = Scene("a", "The Right Title");

            Mocker.GetMock<IAddMovieService>()
                .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), false, true))
                .Returns(new List<Movie> { movie });
            GivenLinkOutcome(LinkOutcome.Linked);

            Subject.Link(7, "a", ItemType.Scene, 4, false);

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(
                It.Is<List<Movie>>(l => l[0].Path == "/media/Studio" && l[0].QualityProfileId == 4 && !l[0].Monitored && l[0].MovieMetadata.Value.ItemType == ItemType.Scene),
                false,
                true));
            Mocker.GetMock<IRefreshNewScenes>().Verify(s => s.RefreshNewScenes(It.Is<List<Movie>>(l => l[0] == movie)));
        }

        [TestCase(LinkOutcome.SceneHasOtherFile)]
        [TestCase(LinkOutcome.LinkedElsewhere)]
        [TestCase(LinkOutcome.OutsideRootFolders)]
        public void should_keep_the_row_when_the_link_fails(LinkOutcome outcome)
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByForeignId("a")).Returns(Scene("a", "The Right Title"));
            GivenLinkOutcome(outcome);

            Assert.Throws<NzbDroneClientException>(() => Subject.Link(7, "a", ItemType.Scene, 1, true));

            Mocker.GetMock<IImportFileRepository>().Verify(r => r.Delete(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void should_not_add_a_scene_for_a_file_that_is_gone()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(FilePath)).Returns(false);

            Assert.Throws<NzbDroneClientException>(() => Subject.Link(7, "a", ItemType.Scene, 1, true));

            Mocker.GetMock<IAddMovieService>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_save_the_dismissed_flag()
        {
            Mocker.GetMock<IImportFileRepository>().Setup(r => r.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<ImportFile> { _file });

            Subject.SetDismissed(new List<int> { 7 }, true);

            Mocker.GetMock<IImportFileRepository>().Verify(r => r.SaveDismissed(It.Is<IList<ImportFile>>(l => l[0].Dismissed)));
        }
    }
}
