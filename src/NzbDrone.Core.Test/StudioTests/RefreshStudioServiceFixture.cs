using System;
using System.Collections.Generic;
using System.Net;

using FizzWare.NBuilder;

using FluentAssertions;

using Moq;

using NUnit.Framework;

using NzbDrone.Common.Http;

using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.ImportExclusions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Movies.Studios.Commands;
using NzbDrone.Core.Movies.Studios.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.StudioTests
{
    [TestFixture]
    public class RefreshStudioServiceFixture : CoreTest<RefreshStudioService>
    {
        private Studio _timedOutStudio;
        private Studio _successfulStudio;

        [SetUp]
        public void Setup()
        {
            _timedOutStudio = Builder<Studio>.CreateNew()
                .With(s => s.Id = 1)
                .With(s => s.ForeignId = "timed-out")
                .With(s => s.Title = "Timed Out Studio")
                .With(s => s.Monitored = true)
                .With(s => s.MoviesMonitored = true)
                .Build();

            _successfulStudio = Builder<Studio>.CreateNew()
                .With(s => s.Id = 2)
                .With(s => s.ForeignId = "successful")
                .With(s => s.Title = "Successful Studio")
                .With(s => s.Monitored = true)
                .With(s => s.MoviesMonitored = true)
                .Build();

            Mocker.GetMock<IStudioService>()
                .Setup(s => s.GetById(_timedOutStudio.Id))
                .Returns(_timedOutStudio);

            Mocker.GetMock<IStudioService>()
                .Setup(s => s.GetById(_successfulStudio.Id))
                .Returns(_successfulStudio);

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.GetByStudioForeignId(It.IsAny<string>()))
                .Returns(new List<Movie>());

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.AllMovieStashIds())
                .Returns(new List<string>());

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(s => s.GetAllExclusions())
                .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioInfo(_timedOutStudio.ForeignId))
                .Returns(_timedOutStudio);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioInfo(_successfulStudio.ForeignId))
                .Returns(_successfulStudio);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(_timedOutStudio.ForeignId))
                .Throws(new WebException("Skyhook timeout"));

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(_successfulStudio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string>(),
                    new List<int>()));
        }

        private Studio GivenStudio(bool monitorNewItems)
        {
            var studio = Builder<Studio>.CreateNew()
                .With(s => s.Id = 3)
                .With(s => s.ForeignId = "studio-with-works")
                .With(s => s.Title = "Studio With Works")
                .With(s => s.Monitored = true)
                .With(s => s.MoviesMonitored = true)
                .With(s => s.SearchOnAdd = true)
                .With(s => s.WhisparrMonitorNewItems = monitorNewItems)
                .Build();

            Mocker.GetMock<IStudioService>()
                .Setup(s => s.GetById(studio.Id))
                .Returns(studio);

            // Let the metadata refresh succeed so no unexpected errors are logged.
            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioInfo(studio.ForeignId))
                .Returns(studio);

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.AllMovieTmdbIds())
                .Returns(new List<int>());

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.AllMovieTpdbIds())
                .Returns(new List<string>());

            return studio;
        }

        private void GivenGlobalNewItemMonitoring(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.WhisparrMonitorNewItems)
                .Returns(enabled);
        }

        private Func<string, ScenePage> _fetchPage;
        private Func<string, Movie> _buildStub;
        private Func<bool> _keepGoing;

        private void GivenImporterCapturesCallbacks()
        {
            Mocker.GetMock<ISceneCatalogImporter>()
                .Setup(s => s.ImportScenes(It.IsAny<string>(), It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()))
                .Callback<string, Func<string, ScenePage>, Func<string, Movie>, Func<bool>>((title, fetchPage, buildStub, keepGoing) =>
                {
                    _fetchPage = fetchPage;
                    _buildStub = buildStub;
                    _keepGoing = keepGoing;
                })
                .Returns(1);
        }

        private List<Movie> GivenAddedMoviesAreCaptured()
        {
            var addedMovies = new List<Movie>();

            Mocker.GetMock<IAddMovieService>()
                .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback((List<Movie> movies, bool ignoreErrors, bool refreshInline) => addedMovies.AddRange(movies))
                .Returns((List<Movie> movies, bool ignoreErrors, bool refreshInline) => movies);

            return addedMovies;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_use_monitor_new_items_when_adding_scenes(bool monitorNewItems)
        {
            var studio = GivenStudio(monitorNewItems);

            GivenGlobalNewItemMonitoring(true);
            GivenImporterCapturesCallbacks();

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            var stub = _buildStub("s1");

            stub.ForeignId.Should().Be("s1");
            stub.Monitored.Should().Be(monitorNewItems);
            stub.AddOptions.SearchForMovie.Should().Be(monitorNewItems);
            stub.AddOptions.AddMethod.Should().Be(AddMovieMethod.Studio);
            stub.QualityProfileId.Should().Be(studio.QualityProfileId);
            stub.RootFolderPath.Should().Be(studio.RootFolderPath);
            stub.Tags.Should().BeEquivalentTo(studio.Tags);
        }

        [Test]
        public void should_page_scenes_from_skyhook_for_monitored_studio()
        {
            var studio = GivenStudio(true);

            GivenImporterCapturesCallbacks();

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            _fetchPage("c1");

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(s => s.GetStudioScenePage(studio.ForeignId, "c1"), Times.Once());
        }

        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        public void should_keep_importing_only_while_the_studio_exists_and_is_monitored(bool exists, bool monitored, bool expected)
        {
            var studio = GivenStudio(true);

            GivenImporterCapturesCallbacks();

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            Mocker.GetMock<IStudioService>()
                .Setup(s => s.FindByForeignId(studio.ForeignId))
                .Returns(exists ? new Studio { ForeignId = studio.ForeignId, Monitored = monitored } : null);

            _keepGoing().Should().Be(expected);
        }

        [Test]
        public void should_not_fetch_studio_works_when_movies_are_not_monitored()
        {
            var studio = GivenStudio(true);

            studio.MoviesMonitored = false;

            GivenImporterCapturesCallbacks();

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(s => s.GetStudioWorks(studio.ForeignId), Times.Never());

            Mocker.GetMock<ISceneCatalogImporter>()
                .Verify(s => s.ImportScenes(It.IsAny<string>(), It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()), Times.Once());
        }

        [Test]
        public void should_sync_next_studio_when_scene_paging_fails()
        {
            var request = new HttpRequest("https://api.whisparr.com/v4/scenes/full");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.BadGateway);

            Mocker.GetMock<ISceneCatalogImporter>()
                .Setup(s => s.ImportScenes(_timedOutStudio.Title, It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()))
                .Throws(new HttpException(request, response));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { _timedOutStudio.Id, _successfulStudio.Id }));

            Mocker.GetMock<ISceneCatalogImporter>()
                .Verify(s => s.ImportScenes(_successfulStudio.Title, It.IsAny<Func<string, ScenePage>>(), It.IsAny<Func<string, Movie>>(), It.IsAny<Func<bool>>()), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_use_monitor_new_items_when_adding_tmdb_movies(bool monitorNewItems)
        {
            var studio = GivenStudio(monitorNewItems);
            var addedMovies = GivenAddedMoviesAreCaptured();

            GivenGlobalNewItemMonitoring(true);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.WhisparrMovieMetadataSource)
                .Returns(MovieMetadataType.TMDB);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(studio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string>(),
                    new List<int> { 123456 }));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            addedMovies.Should().HaveCount(1);
            addedMovies.Should().OnlyContain(m => m.Monitored == monitorNewItems);
            addedMovies.Should().OnlyContain(m => m.AddOptions.SearchForMovie == monitorNewItems);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_use_monitor_new_items_when_adding_tpdb_movies(bool monitorNewItems)
        {
            var studio = GivenStudio(monitorNewItems);
            var addedMovies = GivenAddedMoviesAreCaptured();

            GivenGlobalNewItemMonitoring(true);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.WhisparrMovieMetadataSource)
                .Returns(MovieMetadataType.TPDB);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(studio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string> { "654321" },
                    new List<int>()));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            addedMovies.Should().HaveCount(1);
            addedMovies.Should().OnlyContain(m => m.Monitored == monitorNewItems);
            addedMovies.Should().OnlyContain(m => m.AddOptions.SearchForMovie == monitorNewItems);
        }

        [Test]
        public void should_still_discover_and_add_works_when_monitor_new_items_is_false()
        {
            var studio = GivenStudio(false);
            var addedMovies = GivenAddedMoviesAreCaptured();

            GivenGlobalNewItemMonitoring(true);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.WhisparrMovieMetadataSource)
                .Returns(MovieMetadataType.TMDB);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(studio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string>(),
                    new List<int> { 123456 }));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(s => s.GetStudioWorks(studio.ForeignId), Times.Once());

            addedMovies.Should().HaveCount(1);
            addedMovies.Should().OnlyContain(m => !m.Monitored);
        }

        // The global master switch overrides the studio's own setting, so a
        // studio that asks to monitor new items still gets them unmonitored.
        [Test]
        public void should_not_monitor_new_scenes_when_global_new_item_monitoring_is_disabled()
        {
            var studio = GivenStudio(true);

            GivenGlobalNewItemMonitoring(false);
            GivenImporterCapturesCallbacks();

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            var stub = _buildStub("s1");

            stub.Monitored.Should().BeFalse();
            stub.AddOptions.SearchForMovie.Should().BeFalse();
        }

        [Test]
        public void should_not_monitor_new_tmdb_movies_when_global_new_item_monitoring_is_disabled()
        {
            var studio = GivenStudio(true);
            var addedMovies = GivenAddedMoviesAreCaptured();

            GivenGlobalNewItemMonitoring(false);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.WhisparrMovieMetadataSource)
                .Returns(MovieMetadataType.TMDB);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(studio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string>(),
                    new List<int> { 123456 }));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            addedMovies.Should().HaveCount(1);
            addedMovies.Should().OnlyContain(m => !m.Monitored);
            addedMovies.Should().OnlyContain(m => !m.AddOptions.SearchForMovie);
        }

        [Test]
        public void should_not_monitor_new_tpdb_movies_when_global_new_item_monitoring_is_disabled()
        {
            var studio = GivenStudio(true);
            var addedMovies = GivenAddedMoviesAreCaptured();

            GivenGlobalNewItemMonitoring(false);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.WhisparrMovieMetadataSource)
                .Returns(MovieMetadataType.TPDB);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(studio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string> { "654321" },
                    new List<int>()));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            addedMovies.Should().HaveCount(1);
            addedMovies.Should().OnlyContain(m => !m.Monitored);
            addedMovies.Should().OnlyContain(m => !m.AddOptions.SearchForMovie);
        }

        [Test]
        public void should_still_discover_and_add_works_when_global_new_item_monitoring_is_disabled()
        {
            var studio = GivenStudio(true);
            var addedMovies = GivenAddedMoviesAreCaptured();

            GivenGlobalNewItemMonitoring(false);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.WhisparrMovieMetadataSource)
                .Returns(MovieMetadataType.TMDB);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(studio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string>(),
                    new List<int> { 123456 }));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(s => s.GetStudioWorks(studio.ForeignId), Times.Once());

            addedMovies.Should().HaveCount(1);
            addedMovies.Should().OnlyContain(m => !m.Monitored);
            addedMovies.Should().OnlyContain(m => !m.AddOptions.SearchForMovie);
        }

        [Test]
        public void should_leave_the_updated_event_to_studio_service_after_a_refresh()
        {
            var studio = GivenStudio(true);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(studio.ForeignId))
                .Returns((
                    new List<string>(),
                    new List<string>(),
                    new List<int>()));

            Subject.Execute(new RefreshStudiosCommand(new List<int> { studio.Id }));

            // StudioService.Update publishes StudioUpdatedEvent, so a second publish here
            // runs every handler twice, including two concurrent cover downloads and resizes.
            Mocker.GetMock<IStudioService>()
                .Verify(s => s.Update(studio), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                .Verify(s => s.PublishEvent(It.IsAny<StudioUpdatedEvent>()), Times.Never());
        }

        [Test]
        public void should_default_monitor_new_items_to_true_for_new_studios()
        {
            new Studio().WhisparrMonitorNewItems.Should().BeTrue();
        }

        [Test]
        public void should_continue_refreshing_studios_when_skyhook_times_out()
        {
            var command = new RefreshStudiosCommand(
                new List<int>
                {
                    _timedOutStudio.Id,
                    _successfulStudio.Id
                });

            Assert.DoesNotThrow(() => Subject.Execute(command));

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(
                    s => s.GetStudioWorks(_timedOutStudio.ForeignId),
                    Times.Once());

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(
                    s => s.GetStudioWorks(_successfulStudio.ForeignId),
                    Times.Once());

            // The timed out works call is caught and downgraded to a warning so
            // the remaining studios still get refreshed.
            ExceptionVerification.ExpectedWarns(1);
        }

        // A timeout is only one of the ways this call fails. GetStudioWorks
        // suppresses HTTP errors and throws them itself, so anything that is not
        // a 404 arrives as HttpException, which SyncStudioItems does not catch.
        [Test]
        public void should_continue_refreshing_studios_when_skyhook_returns_an_http_error()
        {
            var request = new HttpRequest("https://api.whisparr.com/v4/site/timed-out/works");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.BadGateway);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(_timedOutStudio.ForeignId))
                .Throws(new HttpException(request, response));

            var command = new RefreshStudiosCommand(
                new List<int>
                {
                    _timedOutStudio.Id,
                    _successfulStudio.Id
                });

            Assert.DoesNotThrow(() => Subject.Execute(command));

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(
                    s => s.GetStudioWorks(_successfulStudio.ForeignId),
                    Times.Once());

            // The HttpException is only caught one level up from the works call,
            // so it is logged as an error rather than a warning.
            ExceptionVerification.ExpectedErrors(1);
        }

        // The scheduled refresh takes the other branch of Execute, which walks
        // every studio rather than a caller-supplied list. One bad studio there
        // used to abort the whole run.
        [Test]
        public void should_continue_the_scheduled_refresh_when_skyhook_returns_an_http_error()
        {
            var request = new HttpRequest("https://api.whisparr.com/v4/site/timed-out/works");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.BadGateway);

            Mocker.GetMock<IProvideMovieInfo>()
                .Setup(s => s.GetStudioWorks(_timedOutStudio.ForeignId))
                .Throws(new HttpException(request, response));

            Mocker.GetMock<IStudioService>()
                .Setup(s => s.AllStudioIdsByLastInfoSync())
                .Returns(new List<int> { _timedOutStudio.Id, _successfulStudio.Id });

            Mocker.GetMock<IStudioService>()
                .Setup(s => s.GetStudios(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Studio> { _timedOutStudio, _successfulStudio });

            var command = new RefreshStudiosCommand { Trigger = CommandTrigger.Manual };

            Assert.DoesNotThrow(() => Subject.Execute(command));

            Mocker.GetMock<IProvideMovieInfo>()
                .Verify(
                    s => s.GetStudioWorks(_successfulStudio.ForeignId),
                    Times.Once());

            // Only the timed out studio fails here; the error is logged and the
            // scheduled run carries on to the next studio.
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
