using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.ImportExclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class AddMovieFixture : CoreTest<AddMovieService>
    {
        private MovieMetadata _fakeMovie;

        [SetUp]
        public void Setup()
        {
            _fakeMovie = Builder<MovieMetadata>
                .CreateNew()
                .Build();
        }

        private void GivenValidMovie(int tmdbId)
        {
            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetMovieInfo(tmdbId))
                  .Returns(new System.Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(_fakeMovie, new Studio(), new List<Performer>(), new List<Credit>()));
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetMovieFolder(It.IsAny<Movie>(), null))
                  .Returns<Movie, NamingConfig>((c, n) => c.Title);

            Mocker.GetMock<IAddMovieValidator>()
                  .Setup(s => s.Validate(It.IsAny<Movie>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void should_be_able_to_add_a_movie_without_passing_in_title()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                RootFolderPath = @"C:\Test\Movies"
            };

            GivenValidMovie(newMovie.TmdbId);
            GivenValidPath();

            var series = Subject.AddMovie(newMovie);

            series.Title.Should().Be(_fakeMovie.Title);
        }

        [Test]
        public void should_have_proper_path()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                RootFolderPath = @"C:\Test\Movies"
            };

            GivenValidMovie(newMovie.TmdbId);
            GivenValidPath();

            var series = Subject.AddMovie(newMovie);

            series.Path.Should().Be(Path.Combine(newMovie.RootFolderPath, _fakeMovie.Title));
        }

        [Test]
        public void should_throw_if_movie_validation_fails()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                Path = @"C:\Test\Movie\Title1"
            };

            GivenValidMovie(newMovie.TmdbId);

            Mocker.GetMock<IAddMovieValidator>()
                  .Setup(s => s.Validate(It.IsAny<Movie>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddMovie(newMovie));
        }

        [Test]
        public void should_throw_if_movie_cannot_be_found()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                Path = @"C:\Test\Movie\Title1"
            };

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetMovieInfo(newMovie.TmdbId))
                  .Throws(new MovieNotFoundException("Movie Not Found"));

            Mocker.GetMock<IAddMovieValidator>()
                  .Setup(s => s.Validate(It.IsAny<Movie>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddMovie(newMovie));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_lock_metadata_sources_once_when_adding_a_movie()
        {
            var newMovie = new Movie
            {
                ForeignId = "1",
                TmdbId = 1,
                RootFolderPath = @"C:\Test\Movies"
            };

            GivenValidMovie(newMovie.TmdbId);
            GivenValidPath();

            Subject.AddMovie(newMovie);

            Mocker.GetMock<IMetadataSourceService>().Verify(v => v.EnsureLocked(), Times.Once());
        }

        [Test]
        public void should_lock_metadata_sources_once_per_batch()
        {
            var newMovies = new List<Movie>
            {
                new Movie { ForeignId = "1", TmdbId = 1, RootFolderPath = @"C:\Test\Movies" },
                new Movie { ForeignId = "2", TmdbId = 2, RootFolderPath = @"C:\Test\Movies" }
            };

            GivenValidMovie(1);
            GivenValidMovie(2);
            GivenValidPath();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMovieForeignIds())
                  .Returns(new List<string>());

            Subject.AddMovies(newMovies);

            Mocker.GetMock<IMetadataSourceService>().Verify(v => v.EnsureLocked(), Times.Once());
        }

        private void GivenValidScene(string foreignId)
        {
            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetSceneInfo(foreignId))
                  .Returns(new System.Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(_fakeMovie, new Studio(), new List<Performer>(), new List<Credit>()));
        }

        private void GivenTpdbSceneFromStudio(string studioForeignId)
        {
            _fakeMovie.ItemType = ItemType.Scene;
            _fakeMovie.MetadataSource = NzbDrone.Core.Movies.MetadataSource.TpdbScene;
            _fakeMovie.ReleaseDateUtc = new System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            _fakeMovie.StudioForeignId = studioForeignId;
            _fakeMovie.Studio = new StudioResource
            {
                Title = "TPDB Studio",
                ForeignIds = new ExternalIdResource { TpdbId = studioForeignId }
            };
        }

        private static Movie TpdbSceneRequest(string sceneUuid)
        {
            // A TPDB scene lookup result carries TpdbId = the scene UUID (MapMovie copies ForeignIds.TpdbId).
            return new Movie
            {
                MovieMetadata = new MovieMetadata
                {
                    ForeignId = sceneUuid,
                    TpdbId = sceneUuid,
                    ItemType = ItemType.Scene
                },
                RootFolderPath = @"C:\Test\Movies"
            };
        }

        [Test]
        public void should_look_up_tpdb_scene_by_foreign_id_not_as_tpdb_movie()
        {
            const string sceneUuid = "0b5e7c2a-1f4d-4e6a-9c3b-2d8f7a6e5c41";

            GivenValidScene(sceneUuid);
            GivenValidPath();

            var movie = Subject.AddMovie(TpdbSceneRequest(sceneUuid));

            movie.Title.Should().Be(_fakeMovie.Title);
            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetSceneInfo(sceneUuid), Times.Once());
            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetTpdbMovieInfo(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_look_up_tpdb_scene_by_foreign_id_when_resolved_metadata_source_is_tpdb_scene()
        {
            const string sceneUuid = "0b5e7c2a-1f4d-4e6a-9c3b-2d8f7a6e5c41";

            GivenValidScene(sceneUuid);
            GivenValidPath();

            var request = TpdbSceneRequest(sceneUuid);
            request.MovieMetadata.Value.ItemType = ItemType.Movie;
            request.MovieMetadata.Value.MetadataSource = NzbDrone.Core.Movies.MetadataSource.TpdbScene;

            Subject.AddMovie(request);

            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetSceneInfo(sceneUuid), Times.Once());
            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetTpdbMovieInfo(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_look_up_stashdb_scene_by_foreign_id()
        {
            const string stashId = "5c1f2a3e-7d4b-4c9a-8e6f-1a2b3c4d5e6f";

            GivenValidScene(stashId);
            GivenValidPath();

            var request = new Movie
            {
                MovieMetadata = new MovieMetadata
                {
                    ForeignId = stashId,
                    StashId = stashId,
                    ItemType = ItemType.Scene
                },
                RootFolderPath = @"C:\Test\Movies"
            };

            Subject.AddMovie(request);

            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetSceneInfo(stashId), Times.Once());
            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetTpdbMovieInfo(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetMovieInfo(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_exclude_tpdb_scene_from_excluded_studio()
        {
            const string sceneUuid = "0b5e7c2a-1f4d-4e6a-9c3b-2d8f7a6e5c41";
            const string studioUuid = "9a8b7c6d-5e4f-4a3b-2c1d-0e9f8a7b6c5d";

            GivenTpdbSceneFromStudio(studioUuid);
            GivenValidScene(sceneUuid);
            GivenValidPath();

            Mocker.GetMock<IImportListExclusionService>()
                  .Setup(s => s.IsExcluded(studioUuid, ImportExclusionType.Studio))
                  .Returns(true);

            Assert.Throws<ExcludedException>(() => Subject.AddMovie(TpdbSceneRequest(sceneUuid)));

            Mocker.GetMock<IImportListExclusionService>()
                  .Verify(v => v.AddExclusion(It.Is<ImportListExclusion>(e => e.Reason == ImportExclusionReason.StudioExclusion)), Times.Once());
        }

        [Test]
        public void should_apply_studio_after_date_to_tpdb_scene()
        {
            const string sceneUuid = "0b5e7c2a-1f4d-4e6a-9c3b-2d8f7a6e5c41";
            const string studioUuid = "9a8b7c6d-5e4f-4a3b-2c1d-0e9f8a7b6c5d";

            GivenTpdbSceneFromStudio(studioUuid);
            GivenValidScene(sceneUuid);
            GivenValidPath();

            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.FindByForeignId(studioUuid))
                  .Returns(new Studio { ForeignId = studioUuid, AfterDate = new System.DateTime(2021, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) });

            Assert.Throws<ExcludedException>(() => Subject.AddMovie(TpdbSceneRequest(sceneUuid)));

            Mocker.GetMock<IImportListExclusionService>()
                  .Verify(v => v.AddExclusion(It.Is<ImportListExclusion>(e => e.Reason == ImportExclusionReason.StudioAfterDate)), Times.Once());
        }
    }
}
