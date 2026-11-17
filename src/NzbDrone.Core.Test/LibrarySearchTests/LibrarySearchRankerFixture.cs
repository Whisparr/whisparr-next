using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.LibrarySearch;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LibrarySearchTests
{
    [TestFixture]
    public class LibrarySearchRankerFixture : CoreTest
    {
        [TestCase("Anna Bell", "Anna Bell")]
        [TestCase("anna bell", "Anna Bell")]
        [TestCase("Anna-Bell", "Anna Bell")]
        [TestCase("Chloé", "Chloe")]
        public void should_score_exact_clean_match_as_exact(string query, string title)
        {
            ScoreMovie(query, title).Should().Be(new LibrarySearchScore(LibrarySearchTier.Exact, 0));
        }

        [Test]
        public void should_score_foreign_id_match_as_exact()
        {
            ScoreMovie("abc-123", "Some Scene", "abc-123").Tier.Should().Be(LibrarySearchTier.Exact);
        }

        [TestCase("Anna", "Anna Goes To The Beach")]
        [TestCase("Anna Go", "Anna Goes To The Beach")]
        public void should_score_title_start_as_prefix(string query, string title)
        {
            ScoreMovie(query, title).Tier.Should().Be(LibrarySearchTier.Prefix);
        }

        [TestCase("Beach", "Anna Goes To The Beach")]
        [TestCase("Goes To", "Anna Goes To The Beach")]
        [TestCase("Bell", "Anna-Bell Rides")]
        public void should_score_word_start_as_word_start(string query, string title)
        {
            ScoreMovie(query, title).Tier.Should().Be(LibrarySearchTier.WordStart);
        }

        [TestCase("each", "Anna Goes To The Beach")]
        [TestCase("nna", "Anna Goes To The Beach")]
        [TestCase("each", "")]
        public void should_score_mid_word_as_substring(string query, string title)
        {
            ScoreMovie(query, title).Tier.Should().Be(LibrarySearchTier.Substring);
        }

        [Test]
        public void should_rank_by_tier_before_distance()
        {
            Rank("anna", "Hannah", "Anna Goes To The Beach", "Anna", "Meet Anna")
                .Should().Equal("Anna", "Anna Goes To The Beach", "Meet Anna", "Hannah");
        }

        [Test]
        public void should_rank_by_distance_within_a_tier()
        {
            Rank("anna", "Anna Goes To The Beach", "Anna Bell")
                .Should().Equal("Anna Bell", "Anna Goes To The Beach");
        }

        [Test]
        public void should_rank_by_title_when_tier_and_distance_tie()
        {
            Rank("anna", "Anna Zed", "Anna Abe")
                .Should().Equal("Anna Abe", "Anna Zed");
        }

        [Test]
        public void should_rank_studios_with_their_own_clean()
        {
            var studios = new[] { "Brazzers Exxtra", "Brazzers" };

            LibrarySearchRanker.Rank(studios, "Brazzers", s => s, s => s.CleanStudioTitle().ToLowerInvariant(), _ => null, s => s.CleanStudioTitle().ToLowerInvariant())[0]
                .Should().Be("Brazzers");
        }

        [TestCase("GAR-204")]
        [TestCase("gar204")]
        [TestCase("gar 204")]
        public void should_score_exact_code_as_exact(string query)
        {
            ScoreMovie(query, "A Long English Title Nothing Alike", code: "GAR-204")
                .Should().Be(new LibrarySearchScore(LibrarySearchTier.Exact, 0));
        }

        [Test]
        public void should_score_code_start_as_prefix_with_remaining_length_as_distance()
        {
            ScoreMovie("GAR", "A Long English Title Nothing Alike", code: "GAR-204")
                .Should().Be(new LibrarySearchScore(LibrarySearchTier.Prefix, 3));
        }

        [Test]
        public void should_not_score_code_infix_as_a_code_hit()
        {
            ScoreMovie("204", "A Long English Title Nothing Alike", code: "GAR-204").Tier.Should().Be(LibrarySearchTier.Substring);
        }

        [Test]
        public void should_keep_better_title_score_over_code_score()
        {
            ScoreMovie("GAR", "Gar Goes Home", code: "GARX-204").Tier.Should().Be(LibrarySearchTier.Prefix);
            ScoreMovie("Gar Goes Home", "Gar Goes Home", code: "GAR-204").Tier.Should().Be(LibrarySearchTier.Exact);
        }

        [Test]
        public void should_rank_exact_code_before_title_substring()
        {
            var items = new[]
            {
                (Title: "Biggar204 Party", Code: (string)null),
                (Title: "A Long English Title", Code: "GAR-204")
            };

            LibrarySearchRanker.Rank(items, "gar204", i => i.Title, i => i.Title.CleanMovieTitle(), _ => null, t => t.CleanMovieTitle(), i => i.Code)
                .Select(i => i.Code).Should().Equal("GAR-204", null);
        }

        private static LibrarySearchScore ScoreMovie(string query, string title, string foreignId = null, string code = null)
        {
            return LibrarySearchRanker.Score(query, query.CleanMovieTitle(), title, title.CleanMovieTitle(), foreignId, t => t.CleanMovieTitle(), code);
        }

        private static List<string> Rank(string query, params string[] titles)
        {
            return LibrarySearchRanker.Rank(titles, query, t => t, t => t.CleanMovieTitle(), _ => null, t => t.CleanMovieTitle());
        }
    }
}
