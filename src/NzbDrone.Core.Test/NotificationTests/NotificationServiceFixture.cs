using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    [TestFixture]
    public class NotificationServiceFixture : CoreTest<NotificationService>
    {
        private Mock<INotification> _notification;
        private NotificationDefinition _definition;

        [SetUp]
        public void Setup()
        {
            _definition = new NotificationDefinition
            {
                Id = 1,
                Name = "Test Notification",
                Tags = new HashSet<int>()
            };

            _notification = new Mock<INotification>();
            _notification.SetupGet(v => v.Definition).Returns(_definition);

            Mocker.GetMock<INotificationFactory>()
                  .Setup(v => v.OnManualInteractionEnabled(It.IsAny<bool>()))
                  .Returns(new List<INotification> { _notification.Object });
        }

        private ManualInteractionRequiredEvent GivenEvent(RemoteMovie remoteMovie)
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    Title = "Some.Unknown.Release-Group",
                    TotalSize = 1234
                },
                RemoteMovie = remoteMovie
            };

            return new ManualInteractionRequiredEvent(trackedDownload, null);
        }

        [Test]
        public void should_send_notification_for_unknown_movie_when_no_tags_are_set()
        {
            Subject.Handle(GivenEvent(null));

            _notification.Verify(v => v.OnManualInteractionRequired(It.IsAny<ManualInteractionRequiredMessage>()), Times.Once());
        }

        [Test]
        public void should_not_send_notification_for_unknown_movie_when_tags_are_set()
        {
            _definition.Tags = new HashSet<int> { 1 };

            Subject.Handle(GivenEvent(null));

            _notification.Verify(v => v.OnManualInteractionRequired(It.IsAny<ManualInteractionRequiredMessage>()), Times.Never());
        }

        [Test]
        public void should_send_notification_when_parsed_movie_info_is_null()
        {
            var remoteMovie = new RemoteMovie
            {
                Movie = new Movie { Id = 1, Tags = new HashSet<int>() },
                ParsedMovieInfo = null
            };

            Subject.Handle(GivenEvent(remoteMovie));

            _notification.Verify(v => v.OnManualInteractionRequired(It.IsAny<ManualInteractionRequiredMessage>()), Times.Once());
        }

        [TestCase(ItemType.Scene, Movies.MetadataSource.Stash, "https://stashdb.org/scenes/6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41", "theporndb.net")]
        [TestCase(ItemType.Scene, Movies.MetadataSource.TpdbScene, "https://theporndb.net/scenes/6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41", "stashdb.org")]
        [TestCase(ItemType.Movie, Movies.MetadataSource.Tmdb, "https://www.imdb.com/title/tt0133093/", "/scenes/")]
        public void should_link_message_to_the_item_source(ItemType itemType, Movies.MetadataSource source, string expectedUrl, string unexpected)
        {
            var remoteMovie = new RemoteMovie
            {
                Movie = new Movie
                {
                    Id = 1,
                    Tags = new HashSet<int>(),
                    MovieMetadata = new MovieMetadata
                    {
                        Title = "Title",
                        ItemType = itemType,
                        MetadataSource = source,
                        ForeignId = "6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41",
                        ImdbId = "tt0133093"
                    }
                }
            };

            ManualInteractionRequiredMessage sent = null;

            _notification.Setup(v => v.OnManualInteractionRequired(It.IsAny<ManualInteractionRequiredMessage>()))
                         .Callback<ManualInteractionRequiredMessage>(m => sent = m);

            Subject.Handle(GivenEvent(remoteMovie));

            sent.Should().NotBeNull();
            sent.Message.Should().EndWith(expectedUrl);
            sent.Message.Should().NotContain(unexpected);
        }
    }
}
