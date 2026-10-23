using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.Fingerprints
{
    [TestFixture]
    public class LinkedFileVerifierFixture : CoreTest<LinkedFileVerifier>
    {
        private List<Movie> _movies;
        private Dictionary<string, FingerprintLookupResult> _results;
        private List<IReadOnlyCollection<FileFingerprint>> _lookups;
        private List<bool> _localOnlyFlags;
        private List<List<MovieFile>> _saves;

        [SetUp]
        public void Setup()
        {
            _movies = new List<Movie>
            {
                new() { Id = 5, ForeignId = "Scene-A" },
                new() { Id = 6, ForeignId = "scene-b" }
            };
            _results = new Dictionary<string, FingerprintLookupResult>();
            _lookups = new List<IReadOnlyCollection<FileFingerprint>>();
            _localOnlyFlags = new List<bool>();
            _saves = new List<List<MovieFile>>();

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                .Returns(() => _movies);

            Mocker.GetMock<IFingerprintLookupClient>()
                .Setup(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()))
                .Callback<IReadOnlyCollection<FileFingerprint>, bool>((f, l) =>
                {
                    _lookups.Add(f);
                    _localOnlyFlags.Add(l);
                })
                .Returns(() => _results);

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.SaveMatchTiers(It.IsAny<List<MovieFile>>()))
                .Callback<List<MovieFile>>(l => _saves.Add(l.ToList()));
        }

        private static MovieFile File(int id, int movieId = 5, MatchTier tier = MatchTier.Name)
        {
            return new MovieFile { Id = id, MovieId = movieId, PHash = $"ph{id}", MatchTier = tier };
        }

        private void GivenResult(int id, FingerprintLookupStatus status, params (string SceneId, int Distance)[] candidates)
        {
            _results[id.ToString()] = new FingerprintLookupResult
            {
                Key = id.ToString(),
                Status = status,
                Candidates = candidates
                    .Select(c => new FingerprintCandidateResult { SceneId = c.SceneId, Algorithm = "PHASH", Distance = c.Distance })
                    .ToList()
            };
        }

        [Test]
        public void should_look_up_local_only_with_the_phash_only_keyed_by_id()
        {
            Subject.Verify(new List<MovieFile> { File(1), File(2) });

            _localOnlyFlags.Should().Equal(true);
            _lookups.Should().ContainSingle().Which
                .Select(f => (f.Key, f.OsHash, f.PHash))
                .Should().Equal(("1", null, "ph1"), ("2", null, "ph2"));
        }

        [Test]
        public void should_look_up_in_batches_of_100()
        {
            Subject.Verify(Enumerable.Range(1, 250).Select(i => File(i)).ToList());

            _lookups.Select(l => l.Count).Should().Equal(100, 100, 50);
        }

        [Test]
        public void should_load_the_movies_in_one_call()
        {
            Subject.Verify(Enumerable.Range(1, 250).Select(i => File(i, i % 2 == 0 ? 5 : 6)).ToList());

            Mocker.GetMock<IMovieService>().Verify(s => s.GetMovies(It.IsAny<IEnumerable<int>>()), Times.Once());
        }

        [TestCase(MatchTier.Name)]
        [TestCase(MatchTier.Unknown)]
        public void should_confirm_when_a_candidate_is_the_linked_scene_ignoring_case(MatchTier from)
        {
            var file = File(1, tier: from);
            GivenResult(1, FingerprintLookupStatus.Matched, ("scene-a", 2));

            Subject.Verify(new List<MovieFile> { file });

            file.MatchTier.Should().Be(MatchTier.Confirmed);
            _saves.Should().ContainSingle().Which.Should().Equal(file);
        }

        [Test]
        public void should_flag_a_conflict_when_the_candidate_is_a_different_scene_and_not_relink()
        {
            var file = File(1);
            GivenResult(1, FingerprintLookupStatus.Matched, ("scene-b", 1));

            Subject.Verify(new List<MovieFile> { file });

            file.MatchTier.Should().Be(MatchTier.Conflict);
            file.MovieId.Should().Be(5);
            _saves.Should().ContainSingle().Which.Should().Equal(file);
        }

        [Test]
        public void should_confirm_when_the_linked_scene_is_among_several_candidates()
        {
            var file = File(1);
            GivenResult(1, FingerprintLookupStatus.Matched, ("scene-b", 1), ("scene-a", 3));

            Subject.Verify(new List<MovieFile> { file });

            file.MatchTier.Should().Be(MatchTier.Confirmed);
        }

        [Test]
        public void should_ignore_candidates_further_than_8()
        {
            var file = File(1);
            GivenResult(1, FingerprintLookupStatus.Matched, ("scene-b", 9));

            Subject.Verify(new List<MovieFile> { file });

            file.MatchTier.Should().Be(MatchTier.Name);
            _saves.Should().BeEmpty();
        }

        [TestCase(FingerprintLookupStatus.NotFound)]
        [TestCase(FingerprintLookupStatus.TryLater)]
        public void should_leave_files_unchanged_without_candidates(FingerprintLookupStatus status)
        {
            var file = File(1);
            GivenResult(1, status);

            Subject.Verify(new List<MovieFile> { file });

            file.MatchTier.Should().Be(MatchTier.Name);
            _saves.Should().BeEmpty();
        }

        [Test]
        public void should_leave_files_unchanged_when_the_result_is_missing()
        {
            var file = File(1);

            Subject.Verify(new List<MovieFile> { file });

            file.MatchTier.Should().Be(MatchTier.Name);
            _saves.Should().BeEmpty();
        }

        [TestCase(MatchTier.Hash)]
        [TestCase(MatchTier.Manual)]
        [TestCase(MatchTier.Confirmed)]
        [TestCase(MatchTier.Conflict)]
        public void should_not_touch_settled_tiers(MatchTier tier)
        {
            var file = File(1, tier: tier);
            GivenResult(1, FingerprintLookupStatus.Matched, ("scene-b", 1));

            Subject.Verify(new List<MovieFile> { file });

            file.MatchTier.Should().Be(tier);
            Mocker.GetMock<IFingerprintLookupClient>()
                .Verify(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_skip_unlinked_files_and_files_without_a_phash()
        {
            var unlinked = File(1, movieId: 0);
            var noHash = File(2);
            noHash.PHash = null;

            Subject.Verify(new List<MovieFile> { unlinked, noHash });

            Mocker.GetMock<IFingerprintLookupClient>()
                .Verify(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_save_every_changed_file_in_one_call_and_skip_the_rest()
        {
            var confirmed = File(1, movieId: 5);
            var conflict = File(2, movieId: 5);
            var unchanged = File(3, movieId: 6);
            GivenResult(1, FingerprintLookupStatus.Matched, ("scene-a", 0));
            GivenResult(2, FingerprintLookupStatus.Matched, ("scene-b", 0));
            GivenResult(3, FingerprintLookupStatus.NotFound);

            Subject.Verify(new List<MovieFile> { confirmed, conflict, unchanged });

            _saves.Should().ContainSingle().Which.Should().Equal(confirmed, conflict);
        }
    }
}
