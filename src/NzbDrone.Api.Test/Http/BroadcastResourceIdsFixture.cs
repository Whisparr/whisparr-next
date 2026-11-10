using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Whisparr.Http;

namespace NzbDrone.Api.Test.Http
{
    [TestFixture]
    public class BroadcastResourceIdsFixture : TestBase<BroadcastResourceIds>
    {
        private List<SignalRMessage> _sent;

        [SetUp]
        public void Setup()
        {
            _sent = new List<SignalRMessage>();

            Mocker.GetMock<IBroadcastSignalRMessage>().Setup(s => s.IsConnected).Returns(true);
            Mocker.GetMock<IBroadcastSignalRMessage>()
                .Setup(s => s.BroadcastMessage(It.IsAny<SignalRMessage>()))
                .Callback<SignalRMessage>(m => _sent.Add(m));
        }

        private static int[] Ids(SignalRMessage message)
        {
            return ((ResourceChangeMessage<IdResource>)message.Body).Resources.Select(r => r.Id).OrderBy(i => i).ToArray();
        }

        [Test]
        public void should_collapse_duplicate_ids_into_one_message()
        {
            Subject.Queue("movie", ModelAction.Updated, new[] { 1, 2 });
            Subject.Queue("movie", ModelAction.Updated, new[] { 2, 3, 1 });

            Subject.Flush();

            _sent.Should().ContainSingle();
            Ids(_sent[0]).Should().Equal(1, 2, 3);
            _sent[0].Name.Should().Be("movie");
            _sent[0].Action.Should().Be(ModelAction.Updated);
            _sent[0].Version.Should().Be(5);
        }

        [Test]
        public void should_send_separate_messages_per_name_and_action()
        {
            Subject.Queue("movie", ModelAction.Updated, new[] { 1 });
            Subject.Queue("movie", ModelAction.Deleted, new[] { 2 });
            Subject.Queue("moviefile", ModelAction.Updated, new[] { 3 });

            Subject.Flush();

            _sent.Should().HaveCount(3);
            _sent.Single(m => m.Name == "movie" && m.Action == ModelAction.Updated).Should().Match<SignalRMessage>(m => Ids(m).SequenceEqual(new[] { 1 }));
            _sent.Single(m => m.Name == "movie" && m.Action == ModelAction.Deleted).Should().Match<SignalRMessage>(m => Ids(m).SequenceEqual(new[] { 2 }));
            _sent.Single(m => m.Name == "moviefile").Should().Match<SignalRMessage>(m => Ids(m).SequenceEqual(new[] { 3 }));
        }

        [Test]
        public void should_send_nothing_and_drop_pending_when_disconnected()
        {
            Mocker.GetMock<IBroadcastSignalRMessage>().Setup(s => s.IsConnected).Returns(false);

            Subject.Queue("movie", ModelAction.Updated, new[] { 1 });
            Subject.Flush();

            Mocker.GetMock<IBroadcastSignalRMessage>().Setup(s => s.IsConnected).Returns(true);
            Subject.Flush();

            _sent.Should().BeEmpty();
        }

        [Test]
        public void should_clear_pending_after_flush()
        {
            Subject.Queue("movie", ModelAction.Updated, new[] { 1 });
            Subject.Flush();
            Subject.Flush();

            _sent.Should().ContainSingle();
        }

        [Test]
        public void should_flush_by_itself_after_the_interval()
        {
            Subject.Queue("movie", ModelAction.Updated, new[] { 1 });

            var deadline = System.DateTime.UtcNow.AddSeconds(5);
            while (_sent.Count == 0 && System.DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(50);
            }

            _sent.Should().ContainSingle();
        }

        [Test]
        public void should_serialise_ids_like_other_resources()
        {
            Subject.Queue("movie", ModelAction.Updated, new[] { 7 });
            Subject.Flush();

            System.Text.Json.JsonSerializer.Serialize(((ResourceChangeMessage<IdResource>)_sent[0].Body).Resources[0], new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                .Should().Be("{\"id\":7}");
        }
    }
}
