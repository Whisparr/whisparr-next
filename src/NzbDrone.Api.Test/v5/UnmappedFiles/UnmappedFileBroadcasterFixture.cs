using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.UnmappedFiles;
using Whisparr.Http;

namespace NzbDrone.Api.Test.v5.UnmappedFiles
{
    [TestFixture]
    public class UnmappedFileBroadcasterFixture : TestBase<UnmappedFileBroadcaster>
    {
        private SignalRMessage _broadcast;

        [SetUp]
        public void Setup()
        {
            _broadcast = null;

            Mocker.GetMock<IBroadcastSignalRMessage>().Setup(s => s.IsConnected).Returns(true);
            Mocker.GetMock<IBroadcastSignalRMessage>()
                .Setup(s => s.BroadcastMessage(It.IsAny<SignalRMessage>()))
                .Callback<SignalRMessage>(m => _broadcast = m);
        }

        private static UnmappedFileMatchCheckedEvent Event(bool sameStudio = true, bool sameDate = true)
        {
            return new UnmappedFileMatchCheckedEvent(new ImportFile
            {
                Id = 9,
                Path = "/media/a.mp4",
                TopMatch = new UnmappedFileMatch { ForeignId = "scene", SameStudio = sameStudio, SameDate = sameDate }
            });
        }

        [Test]
        public void should_broadcast_the_updated_resource_as_unmappedfile()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists("/media/a.mp4")).Returns(true);

            Subject.Handle(Event());

            _broadcast.Name.Should().Be("unmappedfile");
            _broadcast.Version.Should().Be(5);
            var body = _broadcast.Body.Should().BeOfType<ResourceChangeMessage<UnmappedFileResource>>().Subject;
            body.Action.Should().Be(ModelAction.Updated);
            body.Resource.Id.Should().Be(9);
            body.Resource.Exists.Should().BeTrue();
            body.Resource.Match.ForeignId.Should().Be("scene");
        }

        [Test]
        public void should_broadcast_without_a_match_when_it_is_not_strong()
        {
            Subject.Handle(Event(sameDate: false));

            ((ResourceChangeMessage<UnmappedFileResource>)_broadcast.Body).Resource.Match.Should().BeNull();
        }

        [Test]
        public void should_not_broadcast_without_a_connection()
        {
            Mocker.GetMock<IBroadcastSignalRMessage>().Setup(s => s.IsConnected).Returns(false);

            Subject.Handle(Event());

            _broadcast.Should().BeNull();
        }
    }
}
