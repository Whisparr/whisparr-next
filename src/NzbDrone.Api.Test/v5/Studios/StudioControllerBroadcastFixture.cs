using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Movies.Studios.Events;
using NzbDrone.Core.MovieStats;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Studios;
using Whisparr.Http;

namespace NzbDrone.Api.Test.v5.Studios
{
    [Parallelizable(ParallelScope.Self)]
    public class StudioControllerBroadcastFixture : TestBase<StudioController>
    {
        private const string ForeignId = "studio-foreign-id";

        [SetUp]
        public void Setup()
        {
            var cache = Mocker.Resolve<ICacheManager>()
                .GetCache<StudioResource>(typeof(StudioResource), "studioResources");
            cache.Set(ForeignId, new StudioResource { Id = 1, ForeignId = ForeignId });

            Mocker.GetMock<IMovieService>()
                .Setup(s => s.GetByStudioForeignId(ForeignId))
                .Returns(new List<Movie>());
            Mocker.GetMock<IMovieStatisticsService>()
                .Setup(s => s.MovieStatistics(It.IsAny<List<int>>()))
                .Returns(new List<MovieStatistics>());
        }

        [Test]
        public void should_link_movies_when_broadcasting_a_studio_update()
        {
            var studio = new Studio { Id = 1, ForeignId = ForeignId, Title = "Studio" };

            // The counts on the model are transient: the IHandleAsync side of the event
            // fills them in after this handler runs, so they are still zero here.
            Mocker.GetMock<IMovieService>()
                .Setup(s => s.GetByStudioForeignId(ForeignId))
                .Returns(new List<Movie>
                {
                    new Movie
                    {
                        Id = 7,
                        MovieMetadata = new MovieMetadata { Year = 2024, ItemType = ItemType.Scene }
                    }
                });

            SignalRMessage broadcast = null;
            Mocker.GetMock<IBroadcastSignalRMessage>()
                .Setup(s => s.IsConnected)
                .Returns(true);
            Mocker.GetMock<IBroadcastSignalRMessage>()
                .Setup(s => s.BroadcastMessage(It.IsAny<SignalRMessage>()))
                .Callback<SignalRMessage>(m => broadcast = m);

            Subject.Handle(new StudioUpdatedEvent(studio));

            broadcast.Should().NotBeNull();
            var resource = ((ResourceChangeMessage<StudioResource>)broadcast.Body).Resource;
            resource.HasScenes.Should().BeTrue();
            resource.HasMovies.Should().BeFalse();
            resource.TotalSceneCount.Should().Be(1);
            resource.Years.Should().BeEquivalentTo(new[] { 2024 });
        }
    }
}
