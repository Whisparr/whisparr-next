using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.Fingerprints
{
    [TestFixture]
    public class FileMatchEvaluatorFixture : CoreTest<FileMatchEvaluator>
    {
        private const string SceneA = "aaaaaaaa-0000-0000-0000-000000000001";
        private const string SceneB = "bbbbbbbb-0000-0000-0000-000000000002";
        private const string SceneC = "cccccccc-0000-0000-0000-000000000003";

        private Dictionary<string, Movie> _library;

        [SetUp]
        public void Setup()
        {
            _library = new Dictionary<string, Movie>(StringComparer.Ordinal);

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.FindByForeignId(It.IsAny<string>()))
                .Returns((string id) => _library.TryGetValue(id, out var movie) ? movie : null);
        }

        private Movie AddToLibrary(int id, string sceneId)
        {
            var movie = new Movie
            {
                Id = id,
                MovieMetadata = new MovieMetadata
                {
                    ForeignId = sceneId,
                    Title = "Title " + id,
                    StudioTitle = "Studio " + id,
                    ReleaseDate = "2026-01-0" + id
                }
            };

            _library[sceneId] = movie;
            return movie;
        }

        private static FingerprintCandidateResult Cand(string sceneId, string algorithm = "OSHASH", int distance = 0, int submissions = 1, int? duration = 944)
        {
            return new FingerprintCandidateResult
            {
                SceneId = sceneId,
                Algorithm = algorithm,
                Distance = distance,
                Submissions = submissions,
                Duration = duration
            };
        }

        private static FingerprintLookupResult Lookup(params FingerprintCandidateResult[] candidates)
        {
            return new FingerprintLookupResult
            {
                Key = "k",
                Status = FingerprintLookupStatus.Matched,
                Candidates = candidates.ToList()
            };
        }

        private void GivenSceneInfo(string sceneId, string title)
        {
            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetSceneInfo(sceneId))
                .Returns(Tuple.Create(
                    new MovieMetadata { ForeignId = sceneId, Title = title, StudioTitle = "Remote Studio", ReleaseDate = "2025-05-05" },
                    (Studio)null,
                    new List<Performer>(),
                    new List<Credit>()));
        }

        [Test]
        public void parser_and_oshash_agree_is_confirmed()
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(a, Lookup(Cand(SceneA)), 944.4);

            outcome.Tier.Should().Be(MatchTier.Confirmed);
            outcome.Target.Should().BeSameAs(a);
            outcome.NeedsReview.Should().BeFalse();
            outcome.Conflict.Should().BeFalse();
        }

        [Test]
        public void parser_and_phash_agree_is_confirmed()
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(a, Lookup(Cand(SceneA, "PHASH", 3)), null);

            outcome.Tier.Should().Be(MatchTier.Confirmed);
            outcome.Target.Should().BeSameAs(a);
        }

        [Test]
        public void parser_agreement_is_case_insensitive()
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(a, Lookup(Cand(SceneA.ToUpperInvariant())), null);

            outcome.Tier.Should().Be(MatchTier.Confirmed);
        }

        [TestCase(FingerprintLookupStatus.NotFound)]
        [TestCase(FingerprintLookupStatus.TryLater)]
        public void parser_without_hash_data_is_name(FingerprintLookupStatus status)
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(a, new FingerprintLookupResult { Status = status }, 944);

            outcome.Tier.Should().Be(MatchTier.Name);
            outcome.Target.Should().BeSameAs(a);
            outcome.NeedsReview.Should().BeFalse();
        }

        [Test]
        public void parser_with_null_lookup_is_name()
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(a, null, 944);

            outcome.Tier.Should().Be(MatchTier.Name);
            outcome.Target.Should().BeSameAs(a);
            outcome.Candidates.Should().BeEmpty();
        }

        [Test]
        public void parser_with_only_distant_phash_is_name()
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(a, Lookup(Cand(SceneB, "PHASH", 9)), 944);

            outcome.Tier.Should().Be(MatchTier.Name);
            outcome.Target.Should().BeSameAs(a);
        }

        [Test]
        public void parser_a_with_library_candidate_b_is_conflict()
        {
            var a = AddToLibrary(1, SceneA);
            AddToLibrary(2, SceneB);

            var outcome = Subject.Evaluate(a, Lookup(Cand(SceneB)), 944);

            outcome.Tier.Should().Be(MatchTier.Conflict);
            outcome.Conflict.Should().BeTrue();
            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
            outcome.Candidates.Select(c => c.SceneId).Should().Contain(SceneB);
        }

        [Test]
        public void parser_a_with_non_library_candidate_b_is_conflict()
        {
            var a = AddToLibrary(1, SceneA);
            GivenSceneInfo(SceneB, "Remote B");

            var outcome = Subject.Evaluate(a, Lookup(Cand(SceneB)), 944);

            outcome.Conflict.Should().BeTrue();
            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
            outcome.Candidates.Single().Title.Should().Be("Remote B");
        }

        [Test]
        public void parser_scene_among_several_candidates_is_confirmed()
        {
            var a = AddToLibrary(1, SceneA);
            AddToLibrary(2, SceneB);

            var outcome = Subject.Evaluate(a, Lookup(Cand(SceneA), Cand(SceneB)), 944);

            outcome.Tier.Should().Be(MatchTier.Confirmed);
            outcome.Target.Should().BeSameAs(a);
            outcome.Conflict.Should().BeFalse();
        }

        [Test]
        public void single_library_oshash_candidate_within_duration_is_hash()
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA, duration: 944)), 944.4);

            outcome.Tier.Should().Be(MatchTier.Hash);
            outcome.Target.Should().BeSameAs(a);
            outcome.NeedsReview.Should().BeFalse();
        }

        [Test]
        public void duration_difference_over_five_seconds_needs_review()
        {
            AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA, duration: 950)), 944.4);

            outcome.Tier.Should().Be(MatchTier.Unknown);
            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
            outcome.Candidates.Should().HaveCount(1);
        }

        [Test]
        public void duration_difference_of_exactly_five_seconds_is_hash()
        {
            AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA, duration: 949)), 944);

            outcome.Tier.Should().Be(MatchTier.Hash);
        }

        [Test]
        public void unknown_file_duration_needs_review()
        {
            AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA)), null);

            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
        }

        [Test]
        public void unknown_candidate_duration_needs_review()
        {
            AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA, duration: null)), 944);

            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
        }

        [Test]
        public void two_oshash_candidates_need_review()
        {
            AddToLibrary(1, SceneA);
            AddToLibrary(2, SceneB);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA), Cand(SceneB)), 944);

            outcome.Tier.Should().Be(MatchTier.Unknown);
            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
            outcome.Candidates.Should().HaveCount(2);
        }

        [Test]
        public void oshash_candidate_not_in_library_needs_review_with_display_info()
        {
            GivenSceneInfo(SceneA, "Remote A");

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA)), 944);

            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();

            var candidate = outcome.Candidates.Single();
            candidate.MovieId.Should().BeNull();
            candidate.Title.Should().Be("Remote A");
            candidate.StudioTitle.Should().Be("Remote Studio");
            candidate.ReleaseDate.Should().Be("2025-05-05");
        }

        [Test]
        public void library_candidate_takes_display_info_from_library_without_fetch()
        {
            AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA, "PHASH", 2)), 944);

            var candidate = outcome.Candidates.Single();
            candidate.MovieId.Should().Be(1);
            candidate.Title.Should().Be("Title 1");
            candidate.StudioTitle.Should().Be("Studio 1");
            candidate.ReleaseDate.Should().Be("2026-01-01");

            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.GetSceneInfo(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void library_lookup_falls_back_to_lowercased_id()
        {
            var a = AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA.ToUpperInvariant())), 944);

            outcome.Tier.Should().Be(MatchTier.Hash);
            outcome.Target.Should().BeSameAs(a);
        }

        [Test]
        public void phash_only_candidate_needs_review()
        {
            AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA, "PHASH", 2)), 944);

            outcome.Tier.Should().Be(MatchTier.Unknown);
            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
            outcome.Candidates.Should().HaveCount(1);
        }

        [Test]
        public void phash_distance_nine_is_dropped()
        {
            AddToLibrary(1, SceneA);

            var outcome = Subject.Evaluate(null, Lookup(Cand(SceneA, "PHASH", 9)), 944);

            outcome.Tier.Should().Be(MatchTier.Unknown);
            outcome.NeedsReview.Should().BeTrue();
            outcome.Candidates.Should().BeEmpty();
        }

        [Test]
        public void phash_distance_eight_is_kept()
        {
            AddToLibrary(1, SceneA);

            Subject.BuildCandidates(Lookup(Cand(SceneA, "PHASH", 8))).Should().HaveCount(1);
        }

        [Test]
        public void no_parser_and_no_hash_data_is_unmatched_review()
        {
            var outcome = Subject.Evaluate(null, new FingerprintLookupResult { Status = FingerprintLookupStatus.TryLater }, 944);

            outcome.Tier.Should().Be(MatchTier.Unknown);
            outcome.NeedsReview.Should().BeTrue();
            outcome.Target.Should().BeNull();
            outcome.Candidates.Should().BeEmpty();
        }

        [Test]
        public void no_parser_with_null_lookup_is_unmatched()
        {
            var outcome = Subject.Evaluate(null, null, 944);

            outcome.Tier.Should().Be(MatchTier.Unknown);
            outcome.Target.Should().BeNull();
            outcome.Candidates.Should().BeEmpty();
        }

        [Test]
        public void both_algorithms_for_one_scene_collapse_preferring_oshash()
        {
            AddToLibrary(1, SceneA);

            var candidates = Subject.BuildCandidates(Lookup(Cand(SceneA, "PHASH", 1), Cand(SceneA, "OSHASH", 0)));

            candidates.Should().HaveCount(1);
            candidates[0].Algorithm.Should().Be("OSHASH");
        }

        [Test]
        public void candidates_are_ordered_oshash_first_then_distance_then_submissions()
        {
            var lookup = Lookup(
                Cand(SceneC, "PHASH", 1, 9),
                Cand("p4", "PHASH", 4, 5),
                Cand("p2-few", "PHASH", 2, 1),
                Cand("p2-many", "PHASH", 2, 7),
                Cand(SceneA, "OSHASH", 0, 2),
                Cand(SceneB, "OSHASH", 0, 6));

            GivenAnySceneInfo();

            var order = Subject.BuildCandidates(lookup).Select(c => c.SceneId).ToList();

            order.Should().Equal(SceneB, SceneA, SceneC, "p2-many", "p2-few", "p4");
        }

        [Test]
        public void scene_info_is_fetched_for_at_most_five_non_library_candidates()
        {
            AddToLibrary(1, SceneA);
            GivenAnySceneInfo();

            var lookup = Lookup(
                Cand(SceneA, "PHASH", 0),
                Cand("s1", "PHASH", 1),
                Cand("s2", "PHASH", 2),
                Cand("s3", "PHASH", 3),
                Cand("s4", "PHASH", 4),
                Cand("s5", "PHASH", 5),
                Cand("s6", "PHASH", 6),
                Cand("s7", "PHASH", 7));

            var candidates = Subject.BuildCandidates(lookup);

            candidates.Should().HaveCount(8);
            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.GetSceneInfo(It.IsAny<string>()), Times.Exactly(5));
            candidates.Single(c => c.SceneId == "s5").Title.Should().NotBeNull();
            candidates.Single(c => c.SceneId == "s6").Title.Should().BeNull();
        }

        [Test]
        public void scene_info_failure_leaves_display_fields_empty()
        {
            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetSceneInfo(It.IsAny<string>()))
                .Throws(new InvalidOperationException("boom"));

            var candidates = Subject.BuildCandidates(Lookup(Cand(SceneA)));

            var candidate = candidates.Single();
            candidate.Title.Should().BeNull();
            candidate.StudioTitle.Should().BeNull();
            candidate.ReleaseDate.Should().BeNull();
        }

        [Test]
        public void studio_title_falls_back_to_studio_entity()
        {
            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetSceneInfo(SceneA))
                .Returns(Tuple.Create(
                    new MovieMetadata { ForeignId = SceneA, Title = "T", ReleaseDate = "2024-04-04" },
                    new Studio { Title = "Entity Studio" },
                    new List<Performer>(),
                    new List<Credit>()));

            Subject.BuildCandidates(Lookup(Cand(SceneA))).Single().StudioTitle.Should().Be("Entity Studio");
        }

        private void GivenAnySceneInfo()
        {
            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetSceneInfo(It.IsAny<string>()))
                .Returns((string id) => Tuple.Create(
                    new MovieMetadata { ForeignId = id, Title = "Remote " + id },
                    (Studio)null,
                    new List<Performer>(),
                    new List<Credit>()));
        }
    }
}
