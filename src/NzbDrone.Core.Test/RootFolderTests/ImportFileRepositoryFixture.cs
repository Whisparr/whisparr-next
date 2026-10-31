using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class ImportFileRepositoryFixture : DbTest<ImportFileRepository, ImportFile>
    {
        [Test]
        public void should_get_only_files_without_oshash_up_to_limit()
        {
            var items = Enumerable.Range(1, 5)
                .Select(i => new ImportFile
                {
                    RootFolderId = 1,
                    Path = $"/import/file{i}.mp4",
                    RelativePath = $"file{i}.mp4",
                    Name = $"file{i}",
                    OsHash = i <= 2 ? "0123456789abcdef" : null
                })
                .ToList();

            Db.InsertMany(items);

            var all = Subject.GetFilesWithoutOsHash(0, 100);

            all.Should().HaveCount(3);
            all.Should().OnlyContain(f => f.OsHash == null);
            Subject.GetFilesWithoutOsHash(0, 2).Should().HaveCount(2);

            var afterFirst = Subject.GetFilesWithoutOsHash(all[0].Id, 100);

            afterFirst.Should().HaveCount(2);
            afterFirst.Should().OnlyContain(f => f.Id > all[0].Id);
        }

        [Test]
        public void should_leave_dismissed_files_out_of_the_active_list_and_filtered_pages()
        {
            Db.InsertMany(new List<ImportFile>
            {
                new ImportFile { RootFolderId = 1, Path = "/media/kept.mp4", RelativePath = "kept.mp4", Name = "kept" },
                new ImportFile { RootFolderId = 1, Path = "/media/hidden.mp4", RelativePath = "hidden.mp4", Name = "hidden", Dismissed = true }
            });

            Subject.FindActiveByRootFolderId(1).Select(f => f.Name).Should().Equal("kept");
            Subject.FindByRootFolderId(1).Should().HaveCount(2);

            var spec = new NzbDrone.Core.Datastore.PagingSpec<ImportFile> { Page = 1, PageSize = 10, SortKey = "path", SortDirection = NzbDrone.Core.Datastore.SortDirection.Ascending };
            var term = "hid";

            spec.FilterExpressions.Add(f => f.Dismissed == true);
            spec.FilterExpressions.Add(f => f.Path.Contains(term));

            var page = Subject.GetPaged(spec);

            page.TotalRecords.Should().Be(1);
            page.Records.Single().Name.Should().Be("hidden");
        }

        [Test]
        public void find_by_path_should_return_the_exact_match_or_null()
        {
            Db.InsertMany(new List<ImportFile>
            {
                new ImportFile { RootFolderId = 1, Path = "/import/a.mp4", RelativePath = "a.mp4", Name = "a" },
                new ImportFile { RootFolderId = 1, Path = "/import/b.mp4", RelativePath = "b.mp4", Name = "b" }
            });

            Subject.FindByPath("/import/b.mp4").Name.Should().Be("b");
            Subject.FindByPath("/import/c.mp4").Should().BeNull();
        }

        private ImportFile GivenStoredFile()
        {
            var file = new ImportFile
            {
                RootFolderId = 1,
                Path = "/import/file.mp4",
                RelativePath = "file.mp4",
                Name = "file"
            };

            Db.Insert(file);

            return file;
        }

        [Test]
        public void should_round_trip_phash_through_save_and_leave_file_out_of_the_phash_queue()
        {
            var file = GivenStoredFile();
            var attemptedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            file.PHash = "abcdef0123";
            file.PHashAttemptedAt = attemptedAt;

            Subject.SavePHashes(new List<ImportFile> { file });

            var stored = Subject.Get(file.Id);
            stored.PHash.Should().Be("abcdef0123");
            stored.PHashAttemptedAt.Should().Be(attemptedAt);
            Subject.GetFilesNeedingPHash(0, 100).Should().NotContain(f => f.Id == file.Id);
        }

        [Test]
        public void phash_save_should_not_touch_oshash_columns()
        {
            var file = GivenStoredFile();
            var hashedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            // A concurrent oshash run stores its columns after this copy of the row was loaded
            var other = Subject.Get(file.Id);
            other.OsHash = "0123456789abcdef";
            other.HashedFileModifiedAt = hashedAt;
            Subject.SaveOsHashes(new List<ImportFile> { other });

            file.PHash = "ff";
            file.PHashAttemptedAt = DateTime.UtcNow;
            Subject.SavePHashes(new List<ImportFile> { file });

            var stored = Subject.Get(file.Id);
            stored.OsHash.Should().Be("0123456789abcdef");
            stored.HashedFileModifiedAt.Should().Be(hashedAt);
            stored.PHash.Should().Be("ff");
        }

        [Test]
        public void oshash_save_should_not_touch_phash_columns()
        {
            var file = GivenStoredFile();
            var attemptedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

            var other = Subject.Get(file.Id);
            other.PHash = "ff";
            other.PHashAttemptedAt = attemptedAt;
            Subject.SavePHashes(new List<ImportFile> { other });

            file.OsHash = "0123456789abcdef";
            file.HashedFileModifiedAt = DateTime.UtcNow;
            Subject.SaveOsHashes(new List<ImportFile> { file });

            var stored = Subject.Get(file.Id);
            stored.PHash.Should().Be("ff");
            stored.PHashAttemptedAt.Should().Be(attemptedAt);
            stored.OsHash.Should().Be("0123456789abcdef");
        }

        [Test]
        public void should_round_trip_candidates_through_save_without_touching_hashes()
        {
            var file = GivenStoredFile();
            var checkedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

            var other = Subject.Get(file.Id);
            other.OsHash = "0123456789abcdef";
            Subject.SaveOsHashes(new List<ImportFile> { other });

            file.Candidates = new List<FingerprintCandidate>
            {
                new() { SceneId = "scene-a", Algorithm = "OSHASH", Distance = 0, Submissions = 3, Duration = 1200, MovieId = 7, Title = "Title", StudioTitle = "Studio", ReleaseDate = "2026-01-02" }
            };
            file.CandidatesCheckedAt = checkedAt;
            Subject.SaveCandidates(new List<ImportFile> { file });

            var stored = Subject.Get(file.Id);
            stored.OsHash.Should().Be("0123456789abcdef");
            stored.CandidatesCheckedAt.Should().Be(checkedAt);
            stored.Candidates.Should().ContainSingle().Which.Should().BeEquivalentTo(file.Candidates[0]);
        }

        [Test]
        public void should_store_an_empty_candidate_list_and_clear_it_back_to_unchecked()
        {
            var file = GivenStoredFile();
            file.Candidates = new List<FingerprintCandidate>();
            file.CandidatesCheckedAt = DateTime.UtcNow;
            Subject.SaveCandidates(new List<ImportFile> { file });

            Subject.Get(file.Id).Candidates.Should().NotBeNull().And.BeEmpty();

            file.CandidatesCheckedAt = null;
            Subject.SaveCandidates(new List<ImportFile> { file });

            Subject.Get(file.Id).CandidatesCheckedAt.Should().BeNull();
        }

        [Test]
        public void should_get_only_hashed_unchecked_files_up_to_limit()
        {
            var items = Enumerable.Range(1, 6)
                .Select(i => new ImportFile
                {
                    RootFolderId = 1,
                    Path = $"/import/file{i}.mp4",
                    RelativePath = $"file{i}.mp4",
                    Name = $"file{i}",
                    OsHash = i == 1 ? null : "0123456789abcdef",
                    CandidatesCheckedAt = i == 2 ? DateTime.UtcNow : null
                })
                .ToList();

            Db.InsertMany(items);

            var pending = Subject.GetFilesNeedingCandidates(0, 100);

            pending.Should().HaveCount(4);
            pending.Should().OnlyContain(f => f.OsHash != null && f.CandidatesCheckedAt == null);
            pending.Select(f => f.Id).Should().BeInAscendingOrder();
            Subject.GetFilesNeedingCandidates(0, 2).Should().HaveCount(2);
            Subject.GetFilesNeedingCandidates(pending[1].Id, 100).Select(f => f.Id).Should().Equal(pending[2].Id, pending[3].Id);
        }

        [Test]
        public void should_get_only_files_needing_phash_up_to_limit()
        {
            var items = Enumerable.Range(1, 5)
                .Select(i => new ImportFile
                {
                    RootFolderId = 1,
                    Path = $"/import/file{i}.mp4",
                    RelativePath = $"file{i}.mp4",
                    Name = $"file{i}",
                    PHash = i == 1 ? "abcdef" : null,
                    PHashAttemptedAt = i == 2 ? DateTime.UtcNow : null
                })
                .ToList();

            Db.InsertMany(items);

            var all = Subject.GetFilesNeedingPHash(0, 100);

            all.Should().HaveCount(3);
            all.Should().OnlyContain(f => f.PHash == null && f.PHashAttemptedAt == null);
            all.Select(f => f.Id).Should().BeInAscendingOrder();
            Subject.GetFilesNeedingPHash(0, 2).Should().HaveCount(2);

            var afterFirst = Subject.GetFilesNeedingPHash(all[0].Id, 100);
            afterFirst.Should().HaveCount(2);
            afterFirst.Should().NotContain(f => f.Id <= all[0].Id);
        }
    }
}
