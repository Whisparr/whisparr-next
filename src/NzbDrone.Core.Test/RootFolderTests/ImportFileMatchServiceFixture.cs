using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class ImportFileMatchServiceFixture : CoreTest<ImportFileMatchService>
    {
        private static readonly DateTime Now = new(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        private List<ImportFile> _files;
        private List<List<ImportFile>> _saves;
        private Dictionary<string, FingerprintLookupResult> _results;

        [SetUp]
        public void Setup()
        {
            Subject.UtcNow = () => Now;
            _saves = new List<List<ImportFile>>();
            _results = new Dictionary<string, FingerprintLookupResult>();
            _files = Enumerable.Range(1, 3)
                .Select(i => new ImportFile { Id = i, OsHash = $"hash{i}", PHash = i == 3 ? "ph3" : null })
                .ToList();

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.GetFilesNeedingCandidates(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(() => _files);

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.SaveCandidates(It.IsAny<List<ImportFile>>()))
                .Callback<List<ImportFile>>(l => _saves.Add(l.ToList()));

            Mocker.GetMock<IFingerprintLookupClient>()
                .Setup(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()))
                .Returns(() => _results);

            Mocker.GetMock<IFileMatchEvaluator>()
                .Setup(e => e.BuildCandidates(It.IsAny<FingerprintLookupResult>()))
                .Returns<FingerprintLookupResult>(r => r.Candidates
                    .Select(c => new FingerprintCandidate { SceneId = c.SceneId, Algorithm = c.Algorithm })
                    .ToList());
        }

        private void GivenResult(int id, FingerprintLookupStatus status, params string[] sceneIds)
        {
            _results[id.ToString()] = new FingerprintLookupResult
            {
                Key = id.ToString(),
                Status = status,
                Candidates = sceneIds.Select(s => new FingerprintCandidateResult { SceneId = s, Algorithm = "OSHASH" }).ToList()
            };
        }

        [Test]
        public void should_ask_for_at_most_500_files()
        {
            Subject.MatchPending();

            Mocker.GetMock<IImportlFileService>().Verify(s => s.GetFilesNeedingCandidates(0, 500), Times.Once());
        }

        [Test]
        public void should_continue_after_a_full_batch_of_try_later_rows_and_wrap_at_the_end()
        {
            var full = Enumerable.Range(1, 500).Select(i => new ImportFile { Id = i, OsHash = "h" }).ToList();
            var rest = new List<ImportFile> { new() { Id = 501, OsHash = "h" } };
            var requested = new List<int>();

            Mocker.GetMock<IImportlFileService>()
                .Setup(s => s.GetFilesNeedingCandidates(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((after, _) =>
                {
                    requested.Add(after);

                    if (after == 0)
                    {
                        return full;
                    }

                    return after == 500 ? rest : new List<ImportFile>();
                });

            Subject.MatchPending();
            Subject.MatchPending();
            Subject.MatchPending();

            requested.Should().Equal(0, 500, 0);
        }

        [Test]
        public void should_look_up_upstream_with_both_hashes_keyed_by_id()
        {
            IReadOnlyCollection<FileFingerprint> sent = null;
            var localOnly = true;

            Mocker.GetMock<IFingerprintLookupClient>()
                .Setup(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()))
                .Callback<IReadOnlyCollection<FileFingerprint>, bool>((f, l) =>
                {
                    sent = f;
                    localOnly = l;
                })
                .Returns(_results);

            Subject.MatchPending();

            localOnly.Should().BeFalse();
            sent.Select(f => (f.Key, f.OsHash, f.PHash)).Should().Equal(("1", "hash1", null), ("2", "hash2", null), ("3", "hash3", "ph3"));
        }

        [Test]
        public void should_store_candidates_and_stamp_matched_files()
        {
            GivenResult(1, FingerprintLookupStatus.Matched, "scene-a", "scene-b");

            Subject.MatchPending();

            _files[0].Candidates.Select(c => c.SceneId).Should().Equal("scene-a", "scene-b");
            _files[0].CandidatesCheckedAt.Should().Be(Now);
        }

        [Test]
        public void should_store_an_empty_list_and_stamp_not_found_files()
        {
            GivenResult(1, FingerprintLookupStatus.NotFound);

            Subject.MatchPending();

            _files[0].Candidates.Should().NotBeNull().And.BeEmpty();
            _files[0].CandidatesCheckedAt.Should().Be(Now);
            Mocker.GetMock<IFileMatchEvaluator>().Verify(e => e.BuildCandidates(It.IsAny<FingerprintLookupResult>()), Times.Never());
        }

        [Test]
        public void should_leave_try_later_and_missing_files_unstamped_and_unsaved()
        {
            GivenResult(1, FingerprintLookupStatus.TryLater);
            GivenResult(2, FingerprintLookupStatus.Matched, "scene-a");

            Subject.MatchPending();

            _saves.Should().ContainSingle().Which.Should().Equal(_files[1]);
            _files[0].CandidatesCheckedAt.Should().BeNull();
            _files[0].Candidates.Should().BeNull();
            _files[2].CandidatesCheckedAt.Should().BeNull();
        }

        [Test]
        public void should_not_save_when_every_lookup_is_try_later()
        {
            GivenResult(1, FingerprintLookupStatus.TryLater);
            GivenResult(2, FingerprintLookupStatus.TryLater);
            GivenResult(3, FingerprintLookupStatus.TryLater);

            Subject.MatchPending();

            Mocker.GetMock<IImportlFileService>().Verify(s => s.SaveCandidates(It.IsAny<List<ImportFile>>()), Times.Never());
        }

        [Test]
        public void should_not_look_up_when_nothing_is_pending()
        {
            _files = new List<ImportFile>();

            Subject.MatchPending();

            Mocker.GetMock<IFingerprintLookupClient>().Verify(c => c.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IImportlFileService>().Verify(s => s.SaveCandidates(It.IsAny<List<ImportFile>>()), Times.Never());
        }
    }
}
