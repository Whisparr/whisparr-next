using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class RefreshMovieServiceFixture : CoreTest<RefreshMovieService>
    {
        private MovieMetadata _movie;
        private Movie _existingMovie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<MovieMetadata>.CreateNew()
                .With(s => s.Status = MovieStatusType.Released)
                .Build();

            _existingMovie = Builder<Movie>.CreateNew()
                .With(s => s.MovieMetadata.Value.Status = MovieStatusType.Released)
                .Build();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_movie.Id))
                  .Returns(_existingMovie);

            Mocker.GetMock<IMovieMetadataService>()
                  .Setup(s => s.Get(_movie.Id))
                  .Returns(_movie);

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetMovieInfo(It.IsAny<int>()))
                  .Callback<int>((i) => { throw new MovieNotFoundException(i); });

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), null))
                  .Returns(string.Empty);

            Mocker.GetMock<IAutoTaggingService>()
                .Setup(s => s.GetTagChanges(_existingMovie))
                .Returns(new AutoTaggingChanges());
        }

        private void GivenNewMovieInfo(MovieMetadata movie, List<Performer> performers = null)
        {
            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetMovieInfo(_movie.TmdbId))
                  .Returns(new System.Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(movie, new Studio(), performers ?? new List<Performer>(), new List<Credit>()));
        }

        [Test]
        public void should_add_co_stars_hidden_and_unmonitored()
        {
            var coStar = new Performer { ForeignId = "co-star-foreign-id", Monitored = true };

            GivenNewMovieInfo(_movie.JsonClone(), new List<Performer> { coStar });

            Subject.Execute(new RefreshMovieCommand(new List<int> { _movie.Id }));

            Mocker.GetMock<IAddPerformerService>()
                .Verify(v => v.AddPerformers(It.Is<List<Performer>>(l => l.Count == 1 &&
                                                                          l[0].ForeignId == "co-star-foreign-id" &&
                                                                          l[0].Visibility == PerformerVisibility.Hidden &&
                                                                          !l[0].Monitored),
                                             true),
                        Times.Once());
        }

        [Test]
        public void should_update_imdb_id_if_changed()
        {
            var newMovieInfo = _movie.JsonClone();
            newMovieInfo.ImdbId = _movie.ImdbId + 1;

            GivenNewMovieInfo(newMovieInfo);

            Subject.Execute(new RefreshMovieCommand(new List<int> { _movie.Id }));

            Mocker.GetMock<IMovieMetadataService>()
                .Verify(v => v.Upsert(It.Is<MovieMetadata>(s => s.ImdbId == newMovieInfo.ImdbId)));
        }

        [Test]
        public void should_log_error_if_tmdb_id_not_found()
        {
            Subject.Execute(new RefreshMovieCommand(new List<int> { _movie.Id }));

            Mocker.GetMock<IMovieMetadataService>()
                .Verify(v => v.Upsert(It.Is<MovieMetadata>(s => s.Status == MovieStatusType.Deleted)), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_update_if_tmdb_id_changed()
        {
            var newMovieInfo = _movie.JsonClone();
            newMovieInfo.ForeignId = _movie.ForeignId + 1;

            GivenNewMovieInfo(newMovieInfo);

            Subject.Execute(new RefreshMovieCommand(new List<int> { _movie.Id }));

            Mocker.GetMock<IMovieMetadataService>()
                .Verify(v => v.Upsert(It.Is<MovieMetadata>(s => s.ForeignId == newMovieInfo.ForeignId)));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_mark_as_deleted_if_tmdb_id_not_found()
        {
            Subject.Execute(new RefreshMovieCommand(new List<int> { _movie.Id }));

            Mocker.GetMock<IMovieMetadataService>()
                .Verify(v => v.Upsert(It.Is<MovieMetadata>(s => s.Status == MovieStatusType.Deleted)), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_remark_as_deleted_if_tmdb_id_not_found()
        {
            _movie.Status = MovieStatusType.Deleted;

            Subject.Execute(new RefreshMovieCommand(new List<int> { _movie.Id }));

            Mocker.GetMock<IMovieMetadataService>()
                .Verify(v => v.Upsert(It.IsAny<MovieMetadata>()), Times.Never());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_continue_refreshing_remaining_movies_when_one_fails()
        {
            var otherMovie = Builder<Movie>.CreateNew()
                .With(s => s.Id = _existingMovie.Id + 1)
                .With(s => s.MovieMetadata.Value.Status = MovieStatusType.Released)
                .Build();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(otherMovie.Id))
                  .Returns(otherMovie);

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetMovieInfo(_existingMovie.Id))
                  .Throws(new IOException());

            Subject.Execute(new RefreshMovieCommand(new List<int> { _existingMovie.Id, otherMovie.Id }));

            // The failing movie used to rethrow, which abandoned every movie queued behind it.
            Mocker.GetMock<IMovieService>()
                  .Verify(v => v.GetMovie(otherMovie.Id), Times.AtLeastOnce());

            Mocker.GetMock<ICommandResultReporter>()
                  .Verify(v => v.Report(CommandResult.Indeterminate), Times.AtLeastOnce());

            // Both builder movies share TmdbId 1, so the mocked failure hits each of them.
            ExceptionVerification.ExpectedErrors(2);
        }

        private List<Movie> GivenNewScenes(int count)
        {
            var scenes = new List<Movie>();

            for (var i = 1; i <= count; i++)
            {
                var id = 100 + i;
                var metadata = new MovieMetadata
                {
                    Id = id,
                    ForeignId = $"scene-{id}",
                    Title = $"Scene {id}",
                    Status = MovieStatusType.Released,
                    MetadataSource = Core.Movies.MetadataSource.Stash
                };

                var scene = new Movie
                {
                    Id = id,
                    MovieMetadataId = id,
                    Path = string.Empty,
                    MovieMetadata = new NzbDrone.Core.Datastore.LazyLoaded<MovieMetadata>(metadata)
                };

                Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(id)).Returns(scene);
                Mocker.GetMock<IMovieMetadataService>().Setup(s => s.Get(id)).Returns(metadata);

                Mocker.GetMock<IProvideMovieInfo>()
                      .Setup(s => s.GetSceneInfo(metadata.ForeignId))
                      .Returns(new System.Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(metadata.JsonClone(), new Studio(), new List<Performer>(), new List<Credit>()));

                scenes.Add(scene);
            }

            return scenes;
        }

        [Test]
        public void should_refresh_new_scenes_without_publishing_movie_updated()
        {
            var scenes = GivenNewScenes(2);

            Subject.RefreshNewScenes(scenes);

            Mocker.GetMock<ICreditService>()
                  .Verify(v => v.UpdateCredits(It.IsAny<List<Credit>>(), It.IsAny<MovieMetadata>()), Times.Exactly(2));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<MovieUpdatedEvent>()), Times.Never());
        }

        [Test]
        public void should_rescan_new_scenes_so_search_on_add_runs()
        {
            var scenes = GivenNewScenes(2);

            Subject.RefreshNewScenes(scenes);

            Mocker.GetMock<IDiskScanService>().Verify(v => v.Scan(scenes[0]), Times.Once());
            Mocker.GetMock<IDiskScanService>().Verify(v => v.Scan(scenes[1]), Times.Once());
        }

        [Test]
        public void should_continue_with_next_scene_when_one_fails()
        {
            var scenes = GivenNewScenes(2);

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetSceneInfo(scenes[0].ForeignId))
                  .Throws(new IOException());

            Subject.RefreshNewScenes(scenes);

            Mocker.GetMock<ICreditService>()
                  .Verify(v => v.UpdateCredits(It.IsAny<List<Credit>>(), It.Is<MovieMetadata>(m => m.Id == scenes[1].MovieMetadataId)), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_still_rescan_new_scene_when_its_refresh_fails()
        {
            var scenes = GivenNewScenes(2);

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetSceneInfo(scenes[0].ForeignId))
                  .Throws(new IOException());

            Subject.RefreshNewScenes(scenes);

            Mocker.GetMock<IDiskScanService>().Verify(v => v.Scan(scenes[0]), Times.Once());
            Mocker.GetMock<IDiskScanService>().Verify(v => v.Scan(scenes[1]), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_publish_movie_updated_when_new_scene_was_deleted_upstream()
        {
            var scenes = GivenNewScenes(1);

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetSceneInfo(scenes[0].ForeignId))
                  .Throws(new MovieNotFoundException(scenes[0].ForeignId));

            Subject.RefreshNewScenes(scenes);

            Mocker.GetMock<IMovieMetadataService>()
                  .Verify(v => v.Upsert(It.Is<MovieMetadata>(m => m.Status == MovieStatusType.Deleted)), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<MovieUpdatedEvent>()), Times.Never());

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
