using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Languages;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.LegacyMigration
{
    // Runs the claim against real rows: FindByFullPath returns a partial MovieFile, which a mock hides
    [TestFixture]
    public class LegacyFileLinkerDbFixture : DbTest<LegacyFileLinker, MovieFile>
    {
        private string _folder;
        private string _file;
        private MediaFileRepository _repository;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\media\Studio".AsOsAgnostic();
            _file = @"C:\media\Studio\GAR204.HQ.wmv".AsOsAgnostic();

            _repository = Mocker.Resolve<MediaFileRepository>();
            Mocker.SetConstant<IMediaFileRepository>(_repository);
            Mocker.SetConstant<IMediaFileService>(Mocker.Resolve<MediaFileService>());

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(_file)).Returns(true);
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.RunLocked(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<Action>()))
                  .Callback<Movie, string, Action>((_, _, action) => action());
        }

        private Movie GivenMovie(string foreignId)
        {
            var metadata = Db.Insert(Builder<MovieMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.ForeignId = foreignId)
                .BuildNew());

            return Db.Insert(Builder<Movie>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.MovieMetadataId = metadata.Id)
                .With(m => m.Path = _folder)
                .With(m => m.MovieFileId = 0)
                .BuildNew());
        }

        [Test]
        public void claiming_a_name_matched_file_keeps_the_rest_of_its_row()
        {
            var guessed = GivenMovie("guessed");
            var target = GivenMovie("target");

            var file = _repository.Insert(Builder<MovieFile>.CreateNew()
                .With(f => f.Id = 0)
                .With(f => f.MovieId = guessed.Id)
                .With(f => f.RelativePath = "GAR204.HQ.wmv")
                .With(f => f.Size = 1234)
                .With(f => f.MatchTier = MatchTier.Name)
                .With(f => f.OsHash = "abc")
                .With(f => f.Quality = new QualityModel(Quality.WEBDL480p))
                .With(f => f.Languages = new List<Language> { Language.Unknown })
                .BuildNew());

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns<IEnumerable<int>>(ids => ids.Contains(guessed.Id) ? new List<Movie> { guessed } : new List<Movie>());

            Subject.Link(target, _file, f => { }, MatchTier.Manual).Should().Be(LinkOutcome.Linked);

            var saved = _repository.Get(file.Id);

            saved.MovieId.Should().Be(target.Id);
            saved.MatchTier.Should().Be(MatchTier.Manual);
            saved.Quality.Quality.Should().Be(Quality.WEBDL480p);
            saved.Size.Should().Be(1234);
            saved.OsHash.Should().Be("abc");
            saved.Languages.Should().Equal(Language.Unknown);
            target.MovieFileId.Should().Be(file.Id);
        }
    }
}
