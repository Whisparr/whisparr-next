using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileRepositoryFixture : DbTest<MediaFileRepository, MovieFile>
    {
        private Movie _movie1;
        private Movie _movie2;

        [SetUp]
        public void Setup()
        {
            _movie1 = Builder<Movie>.CreateNew()
                                    .With(s => s.Id = 7)
                                    .Build();

            _movie2 = Builder<Movie>.CreateNew()
                                    .With(s => s.Id = 8)
                                    .Build();
        }

        [Test]
        public void get_files_by_movie()
        {
            var files = Builder<MovieFile>.CreateListOfSize(10)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel())
                .With(c => c.Languages = new List<Language> { Language.English })
                .Random(4)
                .With(s => s.MovieId = 12)
                .BuildListOfNew();

            Db.InsertMany(files);

            var movieFiles = Subject.GetFilesByMovie(12);

            movieFiles.Should().HaveCount(4);
            movieFiles.Should().OnlyContain(c => c.MovieId == 12);
        }

        [Test]
        public void should_delete_files_by_movieId()
        {
            var items = Builder<MovieFile>.CreateListOfSize(5)
                .TheFirst(1)
                .With(c => c.MovieId = _movie2.Id)
                .TheRest()
                .With(c => c.MovieId = _movie1.Id)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.Bluray1080p))
                .With(c => c.Languages = new List<Language> { Language.English })
                .BuildListOfNew();

            Db.InsertMany(items);

            Subject.DeleteForMovies(new List<int> { _movie1.Id });

            var removedItems = Subject.GetFilesByMovie(_movie1.Id);
            var nonRemovedItems = Subject.GetFilesByMovie(_movie2.Id);

            removedItems.Should().HaveCount(0);
            nonRemovedItems.Should().HaveCount(1);
        }

        [Test]
        public void should_get_only_files_without_oshash_up_to_limit_including_unmapped()
        {
            var items = Builder<MovieFile>.CreateListOfSize(6)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.MovieId = _movie1.Id)
                .With(c => c.Quality = new QualityModel(Quality.Bluray1080p))
                .With(c => c.Languages = new List<Language> { Language.English })
                .With(c => c.OsHash = null)
                .TheFirst(2)
                .With(c => c.OsHash = "0123456789abcdef")
                .TheNext(1)
                .With(c => c.MovieId = 0)
                .BuildListOfNew();

            Db.InsertMany(items);

            var all = Subject.GetFilesWithoutOsHash(0, 100);

            all.Should().HaveCount(4);
            all.Should().OnlyContain(c => c.OsHash == null);
            all.Should().Contain(c => c.MovieId == 0);

            Subject.GetFilesWithoutOsHash(0, 3).Should().HaveCount(3);

            var afterFirst = Subject.GetFilesWithoutOsHash(all[0].Id, 100);

            afterFirst.Should().HaveCount(3);
            afterFirst.Should().OnlyContain(c => c.Id > all[0].Id);
            afterFirst.Select(c => c.Id).Should().BeInAscendingOrder();
        }

        private MovieFile GivenStoredFile()
        {
            var file = Builder<MovieFile>.CreateNew()
                .With(c => c.Id = 0)
                .With(c => c.MovieId = _movie1.Id)
                .With(c => c.Quality = new QualityModel(Quality.Bluray1080p))
                .With(c => c.Languages = new List<Language> { Language.English })
                .With(c => c.OsHash = null)
                .With(c => c.HashedFileModifiedAt = null)
                .With(c => c.PHash = null)
                .With(c => c.PHashAttemptedAt = null)
                .BuildNew();

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

            Subject.SavePHashes(new List<MovieFile> { file });

            var stored = Subject.Get(file.Id);
            stored.PHash.Should().Be("abcdef0123");
            stored.PHashAttemptedAt.Should().Be(attemptedAt);
            Subject.GetFilesNeedingPHash(0, 100).Should().NotContain(f => f.Id == file.Id);
        }

        [Test]
        public void match_tier_save_should_only_write_the_tier()
        {
            var file = GivenStoredFile();
            file.MatchTier = MatchTier.Confirmed;
            file.PHash = "should-not-be-saved";

            Subject.SaveMatchTiers(new List<MovieFile> { file });

            var stored = Subject.Get(file.Id);
            stored.MatchTier.Should().Be(MatchTier.Confirmed);
            stored.PHash.Should().BeNull();
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
            Subject.SaveOsHashes(new List<MovieFile> { other });

            file.PHash = "ff";
            file.PHashAttemptedAt = DateTime.UtcNow;
            Subject.SavePHashes(new List<MovieFile> { file });

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
            Subject.SavePHashes(new List<MovieFile> { other });

            file.OsHash = "0123456789abcdef";
            file.HashedFileModifiedAt = DateTime.UtcNow;
            Subject.SaveOsHashes(new List<MovieFile> { file });

            var stored = Subject.Get(file.Id);
            stored.PHash.Should().Be("ff");
            stored.PHashAttemptedAt.Should().Be(attemptedAt);
            stored.OsHash.Should().Be("0123456789abcdef");
        }

        [Test]
        public void should_get_only_files_needing_phash_up_to_limit()
        {
            var items = Builder<MovieFile>.CreateListOfSize(6)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.MovieId = _movie1.Id)
                .With(c => c.Quality = new QualityModel(Quality.Bluray1080p))
                .With(c => c.Languages = new List<Language> { Language.English })
                .With(c => c.PHash = null)
                .With(c => c.PHashAttemptedAt = null)
                .TheFirst(1)
                .With(c => c.PHash = "abcdef")
                .TheNext(1)
                .With(c => c.PHashAttemptedAt = DateTime.UtcNow)
                .TheNext(1)
                .With(c => c.MovieId = 0)
                .BuildListOfNew();

            Db.InsertMany(items);

            var all = Subject.GetFilesNeedingPHash(0, 100);

            all.Should().HaveCount(4);
            all.Should().OnlyContain(c => c.PHash == null && c.PHashAttemptedAt == null);
            all.Should().Contain(c => c.MovieId == 0);
            all.Select(c => c.Id).Should().BeInAscendingOrder();
            Subject.GetFilesNeedingPHash(0, 3).Should().HaveCount(3);

            var afterFirst = Subject.GetFilesNeedingPHash(all[0].Id, 100);
            afterFirst.Should().HaveCount(3);
            afterFirst.Should().NotContain(c => c.Id <= all[0].Id);
        }

        private MovieFile GivenTrackedFile(string foreignId, string folder, string relativePath)
        {
            var metadata = Db.Insert(Builder<MovieMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.ForeignId = foreignId)
                .BuildNew());

            var movie = Db.Insert(Builder<Movie>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.MovieMetadataId = metadata.Id)
                .With(m => m.Path = folder)
                .BuildNew());

            return Subject.Insert(Builder<MovieFile>.CreateNew()
                .With(f => f.Id = 0)
                .With(f => f.MovieId = movie.Id)
                .With(f => f.RelativePath = relativePath)
                .With(f => f.Quality = new QualityModel())
                .With(f => f.Languages = new List<Language> { Language.English })
                .BuildNew());
        }

        [Test]
        public void find_by_full_path_should_match_movie_folder_plus_relative_path()
        {
            var flat = GivenTrackedFile("flat", "/media/Studio".AsOsAgnostic(), "scene.mp4");
            var nested = GivenTrackedFile("nested", "/media".AsOsAgnostic(), "Studio2/scene.mp4".AsOsAgnostic());

            var foundFlat = Subject.FindByFullPath("/media/Studio/scene.mp4".AsOsAgnostic());
            var foundNested = Subject.FindByFullPath("/media/Studio2/scene.mp4".AsOsAgnostic());

            foundFlat.Id.Should().Be(flat.Id);
            foundFlat.MovieId.Should().Be(flat.MovieId);
            foundNested.Id.Should().Be(nested.Id);
        }

        [Test]
        public void find_by_full_path_should_return_the_stored_match_tier()
        {
            var tracked = GivenTrackedFile("tiered", "/media/Studio".AsOsAgnostic(), "scene.mp4");
            tracked.MatchTier = MatchTier.Name;
            Subject.SaveMatchTiers(new List<MovieFile> { tracked });

            Subject.FindByFullPath("/media/Studio/scene.mp4".AsOsAgnostic()).MatchTier.Should().Be(MatchTier.Name);
        }

        [Test]
        public void find_by_full_path_should_return_null_for_untracked_paths()
        {
            GivenTrackedFile("flat", "/media/Studio".AsOsAgnostic(), "scene.mp4");

            Subject.FindByFullPath("/media/Studio/other.mp4".AsOsAgnostic()).Should().BeNull();
            Subject.FindByFullPath("/media/Elsewhere/scene.mp4".AsOsAgnostic()).Should().BeNull();
        }
    }
}
