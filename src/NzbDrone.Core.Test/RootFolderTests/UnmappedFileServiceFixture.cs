using System;
using System.Collections.Generic;
using System.Globalization;
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
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Jav;
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
            movie.MovieMetadata.Value.ReleaseDateUtc = date == null ? null : DateTime.Parse(date + "T00:00:00Z", CultureInfo.InvariantCulture).ToUniversalTime();

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

        private void GivenJavFile(bool available)
        {
            var path = "/media/jav/ABP-123.mp4";

            Mocker.GetMock<IImportFileRepository>().Setup(r => r.Get(8)).Returns(new ImportFile { Id = 8, RootFolderId = 1, Path = path, Name = "ABP-123.mp4" });
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(available);
        }

        private void GivenTypedSearch(string term, ItemType itemType, params Movie[] results)
        {
            Mocker.GetMock<ISearchForNewMovie>()
                .Setup(s => s.SearchForNewEntity(term, itemType))
                .Returns(results.Cast<object>().ToList());
        }

        [Test]
        public void jav_code_file_suggests_jav_first()
        {
            GivenJavFile(true);
            GivenTypedSearch("ABP-123", ItemType.Jav, Scene("jav", "Unrelated"));
            GivenTypedSearch("ABP-123", ItemType.Scene, Scene("scene", "ABP-123", "2024-03-05"), Scene("jav", "Unrelated"));

            var results = Subject.Suggest(8, null, ItemType.Scene).Results;

            results.Select(r => r.Movie.ForeignId).Should().Equal("jav", "scene");
        }

        [Test]
        public void unhyphenated_code_file_suggests_jav_first()
        {
            Mocker.GetMock<IImportFileRepository>().Setup(r => r.Get(8)).Returns(new ImportFile { Id = 8, RootFolderId = 1, Path = "/media/jav/abp123.mp4", Name = "abp123.mp4" });
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            GivenTypedSearch("ABP-123", ItemType.Jav, Scene("jav", "Unrelated"));

            var results = Subject.Suggest(8, null, ItemType.Scene).Results;

            results.Select(r => r.Movie.ForeignId).Should().StartWith("jav");
        }

        [Test]
        public void jav_unavailable_suggests_scenes_only()
        {
            GivenJavFile(false);
            GivenTypedSearch("ABP-123", ItemType.Scene, Scene("scene", "ABP-123"));

            var results = Subject.Suggest(8, null, ItemType.Scene).Results;

            results.Select(r => r.Movie.ForeignId).Should().Equal("scene");
            Mocker.GetMock<ISearchForNewMovie>().Verify(s => s.SearchForNewEntity(It.IsAny<string>(), ItemType.Jav), Times.Never());
        }

        [Test]
        public void jav_search_failure_keeps_scene_results()
        {
            GivenJavFile(true);
            Mocker.GetMock<ISearchForNewMovie>().Setup(s => s.SearchForNewEntity("ABP-123", ItemType.Jav)).Throws(new Exception("down"));
            GivenTypedSearch("ABP-123", ItemType.Scene, Scene("scene", "ABP-123"));

            Subject.Suggest(8, null, ItemType.Scene).Results.Select(r => r.Movie.ForeignId).Should().Equal("scene");
        }

        [Test]
        public void explicit_jav_suggest_searches_jav_when_available()
        {
            GivenJavFile(true);
            GivenTypedSearch("typed", ItemType.Jav, Scene("jav", "Typed"));

            Subject.Suggest(8, "typed", ItemType.Jav).Results.Select(r => r.Movie.ForeignId).Should().Equal("jav");
        }

        private void GivenFile(int id, string name)
        {
            Mocker.GetMock<IImportFileRepository>().Setup(r => r.Get(id)).Returns(new ImportFile { Id = id, RootFolderId = 1, Path = "/media/jav/" + name, Name = name });
        }

        [TestCase("STARS 080 uncensored leak.mp4", "STARS-080")]
        [TestCase("[FHD] MIDV-018.mp4", "MIDV-018")]
        [TestCase("ipx_100.mkv", "IPX-100")]
        [TestCase("300MIUM-642.mp4", "300MIUM-642")]
        public void explicit_jav_suggest_searches_the_file_code(string name, string code)
        {
            GivenFile(9, name);
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            GivenTypedSearch(code, ItemType.Jav, Scene("jav", "Title Without The Code"));

            var suggestions = Subject.Suggest(9, null, ItemType.Jav);

            suggestions.Term.Should().Be(code);
            suggestions.Results.Select(r => r.Movie.ForeignId).Should().Equal("jav");
        }

        private static Movie Jav(string id, string code, string date = null)
        {
            var movie = Scene(id, "Unrelated Title", date);

            movie.MovieMetadata.Value.ItemType = ItemType.Jav;
            movie.MovieMetadata.Value.Code = code;

            return movie;
        }

        [TestCase("ABP-123", true)]
        [TestCase("abp123", true)]
        [TestCase("ABP-124", false)]
        [TestCase(null, false)]
        public void should_flag_a_result_whose_code_matches_the_file(string resultCode, bool expected)
        {
            GivenJavFile(true);
            GivenTypedSearch("ABP-123", ItemType.Jav, Jav("jav", resultCode));

            Subject.Suggest(8, null, ItemType.Jav).Results.Single().CodeMatch.Should().Be(expected);
        }

        [Test]
        public void should_not_flag_a_code_shared_by_two_results()
        {
            GivenJavFile(true);
            GivenTypedSearch("ABP-123", ItemType.Jav, Jav("first", "ABP-123"), Jav("second", "ABP-123"));

            Subject.Suggest(8, null, ItemType.Jav).Results.Should().OnlyContain(r => !r.CodeMatch);
        }

        [Test]
        public void should_rank_a_code_match_above_a_same_date_result()
        {
            _file.Path = "/media/Studio/ABP-123 Studio - 2024-03-05 - The Right Title.mp4";
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            GivenTypedSearch("ABP-123", ItemType.Jav, Jav("other", "ABP-999", "2024-03-05"), Jav("jav", "ABP-123"));

            var results = Subject.Suggest(7, null, ItemType.Jav).Results;

            results.Select(r => r.Movie.ForeignId).Should().Equal("jav", "other");
        }

        [Test]
        public void should_not_flag_a_code_match_on_a_file_without_a_code()
        {
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);
            GivenSearch("The Right Title", Jav("jav", "ABP-123"));

            Subject.Suggest(7, null, ItemType.Scene).Results.Single().CodeMatch.Should().BeFalse();
        }

        private static Movie Feature(string id, string title, int year, params string[] alternativeTitles)
        {
            var movie = Scene(id, title);

            movie.MovieMetadata.Value.ItemType = ItemType.Movie;
            movie.MovieMetadata.Value.Year = year;
            movie.MovieMetadata.Value.AlternativeTitles = alternativeTitles.Select(t => new AlternativeTitle(t)).ToList();

            return movie;
        }

        [Test]
        public void should_flag_and_rank_a_movie_by_title_and_year()
        {
            GivenFile(9, "The.Movie.Title.2019.1080p.BluRay.mkv");
            GivenSearch("The Movie Title",
                Feature("other-title", "Another Movie", 2019),
                Feature("other-year", "The Movie Title", 2018),
                Feature("exact", "The Movie Title!", 2019));

            var results = Subject.Suggest(9, null, ItemType.Movie).Results;

            results.Select(r => r.Movie.ForeignId).Should().Equal("exact", "other-year", "other-title");
            results[0].SameTitle.Should().BeTrue();
            results[0].SameYear.Should().BeTrue();
            results[1].SameTitle.Should().BeTrue();
            results[1].SameYear.Should().BeFalse();
            results[2].SameTitle.Should().BeFalse();
            results[2].SameYear.Should().BeTrue();
        }

        [Test]
        public void should_match_the_title_on_an_alternative_title()
        {
            GivenFile(9, "The.Movie.Title.2019.1080p.BluRay.mkv");
            GivenSearch("The Movie Title", Feature("alt", "Original Name", 2019, "The Movie Title"));

            Subject.Suggest(9, null, ItemType.Movie).Results.Single().SameTitle.Should().BeTrue();
        }

        [TestCase("Pirates.II.Stagnettis.Revenge.2008.720p.mkv", "Pirates II Stagnettis Revenge", "Pirates 2: Stagnetti's Revenge")]
        [TestCase("Pirates.2.Stagnettis.Revenge.2008.720p.mkv", "Pirates 2 Stagnettis Revenge", "Pirates II: Stagnetti's Revenge")]
        public void should_match_the_title_across_roman_and_arabic_numerals(string name, string term, string title)
        {
            GivenFile(9, name);
            GivenSearch(term, Feature("movie", title, 2008));

            var result = Subject.Suggest(9, null, ItemType.Movie).Results.Single();

            result.SameTitle.Should().BeTrue();
            result.SameYear.Should().BeTrue();
        }

        [TestCase("Saw.21.2021.mkv", "Saw 21", "Saw III")]
        [TestCase("Saw.111.2021.mkv", "Saw 111", "Saw 12")]
        [TestCase("Saw.12.2021.mkv", "Saw 12", "Saw I II")]
        public void should_not_match_a_different_number_rewritten_digit_by_digit(string name, string term, string title)
        {
            GivenFile(9, name);
            GivenSearch(term, Feature("movie", title, 2021));

            Subject.Suggest(9, null, ItemType.Movie).Results.Single().SameTitle.Should().BeFalse();
        }

        [Test]
        public void should_not_flag_a_year_the_file_name_does_not_have()
        {
            GivenFile(9, "The Movie Title.mkv");
            GivenSearch("The Movie Title", Feature("movie", "The Movie Title", 2019));

            Subject.Suggest(9, null, ItemType.Movie).Results.Single().SameYear.Should().BeFalse();
        }

        [Test]
        public void explicit_jav_suggest_falls_back_to_the_title_without_a_code()
        {
            GivenFile(9, "Some Title ABP-456.mp4");
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);

            Subject.Suggest(9, null, ItemType.Jav).Term.Should().Be("Some Title ABP-456");
        }

        [Test]
        public void explicit_jav_suggest_keeps_the_typed_term()
        {
            GivenFile(9, "ABP-123.mp4");
            Mocker.GetMock<IJavAvailability>().Setup(a => a.IsAvailable()).Returns(true);

            Subject.Suggest(9, "momo sakai", ItemType.Jav).Term.Should().Be("momo sakai");
        }

        [TestCase("[FHD] Some Scene Title.mp4", "Some Scene Title")]
        [TestCase("[Tag] (2024) [Other] Some Scene Title [1080p].mp4", "Some Scene Title")]
        [TestCase("[FHD] MIDV-018.mp4", "MIDV-018")]
        public void should_drop_leading_tags_from_the_search_term(string name, string expected)
        {
            GivenFile(9, name);

            Subject.Suggest(9, null, ItemType.Scene).Term.Should().Be(expected);
        }

        [Test]
        public void explicit_jav_suggest_is_empty_when_unavailable()
        {
            GivenJavFile(false);

            Subject.Suggest(8, "typed", ItemType.Jav).Results.Should().BeEmpty();
            Mocker.GetMock<ISearchForNewMovie>().Verify(s => s.SearchForNewEntity(It.IsAny<string>(), It.IsAny<ItemType?>()), Times.Never());
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
                It.Is<List<Movie>>(l => l[0].Path == System.IO.Path.GetDirectoryName(FilePath) && l[0].QualityProfileId == 4 && !l[0].Monitored && l[0].MovieMetadata.Value.ItemType == ItemType.Scene),
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
