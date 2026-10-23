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

            var all = Subject.GetFilesNeedingPHash(100);

            all.Should().HaveCount(4);
            all.Should().OnlyContain(c => c.PHash == null && c.PHashAttemptedAt == null);
            all.Should().Contain(c => c.MovieId == 0);
            all.Select(c => c.Id).Should().BeInAscendingOrder();
            Subject.GetFilesNeedingPHash(3).Should().HaveCount(3);
        }
    }
}
