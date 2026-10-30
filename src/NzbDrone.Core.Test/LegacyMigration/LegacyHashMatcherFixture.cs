using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacyHashMatcherFixture : CoreTest<LegacyHashMatcher>
    {
        private static FingerprintCandidateResult Candidate(string sceneId, string algorithm = "OSHASH")
        {
            return new FingerprintCandidateResult { SceneId = sceneId, Algorithm = algorithm };
        }

        private void GivenLookup(FingerprintLookupStatus status, params FingerprintCandidateResult[] candidates)
        {
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), false))
                  .Returns<IReadOnlyCollection<FileFingerprint>, bool>((files, _) =>
                      files.ToDictionary(f => f.Key, f => new FingerprintLookupResult
                      {
                          Key = f.Key,
                          Status = status,
                          Candidates = candidates.ToList()
                      }));
        }

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IVideoHashService>()
                  .Setup(v => v.ComputeOsHash(It.IsAny<string>()))
                  .Returns("computed");
        }

        [Test]
        public void single_oshash_candidate_matches()
        {
            GivenLookup(FingerprintLookupStatus.Matched, Candidate("s1", "oshash"), Candidate("s1"));

            var result = Subject.Match(new[] { new HashQuery("k", "/a.mp4", null) });

            result["k"].Should().Be(new HashMatch("s1", "computed", false));
        }

        [Test]
        public void two_oshash_scenes_is_no_match()
        {
            GivenLookup(FingerprintLookupStatus.Matched, Candidate("s1"), Candidate("s2"));

            var result = Subject.Match(new[] { new HashQuery("k", "/a.mp4", null) });

            result["k"].Should().Be(new HashMatch(null, "computed", false));
        }

        [Test]
        public void phash_only_candidate_is_no_match()
        {
            GivenLookup(FingerprintLookupStatus.Matched, Candidate("s1", "PHASH"));

            var result = Subject.Match(new[] { new HashQuery("k", "/a.mp4", null) });

            result["k"].SceneId.Should().BeNull();
            result["k"].TryLater.Should().BeFalse();
        }

        [Test]
        public void known_hash_is_not_recomputed()
        {
            GivenLookup(FingerprintLookupStatus.Matched, Candidate("s1"));

            var result = Subject.Match(new[] { new HashQuery("k", "/a.mp4", "known") });

            result["k"].OsHash.Should().Be("known");
            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void hash_failure_is_no_match_without_throwing()
        {
            Mocker.GetMock<IVideoHashService>()
                  .Setup(v => v.ComputeOsHash("/bad.mp4"))
                  .Throws(new InvalidOperationException("boom"));
            Mocker.GetMock<IVideoHashService>()
                  .Setup(v => v.ComputeOsHash("/null.mp4"))
                  .Returns((string)null);
            GivenLookup(FingerprintLookupStatus.Matched, Candidate("s1"));

            var result = Subject.Match(new[]
            {
                new HashQuery("bad", "/bad.mp4", null),
                new HashQuery("null", "/null.mp4", null),
                new HashQuery("ok", "/ok.mp4", null)
            });

            result["bad"].Should().Be(new HashMatch(null, null, false));
            result["null"].Should().Be(new HashMatch(null, null, false));
            result["ok"].SceneId.Should().Be("s1");
        }

        [Test]
        public void try_later_is_flagged()
        {
            GivenLookup(FingerprintLookupStatus.TryLater);

            var result = Subject.Match(new[] { new HashQuery("k", "/a.mp4", null) });

            result["k"].Should().Be(new HashMatch(null, "computed", true));
        }

        [Test]
        public void missing_lookup_entry_is_no_match()
        {
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), false))
                  .Returns(new Dictionary<string, FingerprintLookupResult>());

            var result = Subject.Match(new[] { new HashQuery("k", "/a.mp4", null) });

            result["k"].Should().Be(new HashMatch(null, "computed", false));
        }

        [Test]
        public void empty_input_skips_lookup()
        {
            Subject.Match(new List<HashQuery>()).Should().BeEmpty();

            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
