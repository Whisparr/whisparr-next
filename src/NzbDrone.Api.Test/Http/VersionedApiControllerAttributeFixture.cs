using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.SignalR;
using Whisparr.Http;
using Whisparr.Http.REST;

namespace NzbDrone.Api.Test.Http
{
    [TestFixture]
    public class VersionedApiControllerAttributeFixture
    {
        private Mock<IBroadcastSignalRMessage> _broadcaster;
        private List<SignalRMessage> _messages;

        [SetUp]
        public void Setup()
        {
            _messages = new List<SignalRMessage>();
            _broadcaster = new Mock<IBroadcastSignalRMessage>();
            _broadcaster.SetupGet(b => b.IsConnected).Returns(true);
            _broadcaster.Setup(b => b.BroadcastMessage(It.IsAny<SignalRMessage>()))
                        .Callback<SignalRMessage>(_messages.Add)
                        .Returns(System.Threading.Tasks.Task.CompletedTask);
        }

        [Test]
        public void should_route_v5_controllers_under_api_v5()
        {
            var attr = new V5ApiControllerAttribute("movie");

            attr.Template.Should().Be("api/v5/movie");
            attr.Version.Should().Be(5);
        }

        [Test]
        public void should_route_v3_controllers_under_api_v3()
        {
            var attr = new V3ApiControllerAttribute("movie");

            attr.Template.Should().Be("api/v3/movie");
            attr.Version.Should().Be(3);
        }

        [Test]
        public void should_stamp_controller_version_on_broadcasts()
        {
            var controller = new V3TestController(_broadcaster.Object);

            controller.Handle(new ModelEvent<TestModel>(new TestModel { Id = 1 }, ModelAction.Updated));

            _messages.Should().ContainSingle().Which.Version.Should().Be(3);
        }

        [Test]
        public void should_stamp_controller_version_on_deleted_and_batch_broadcasts()
        {
            var controller = new V5TestController(_broadcaster.Object);

            controller.Handle(new ModelEvent<TestModel>(new TestModel { Id = 1 }, ModelAction.Deleted));
            controller.Batch(new List<TestResource> { new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 } }, 2);

            _messages.Should().HaveCount(4);
            _messages.Should().OnlyContain(m => m.Version == 5);
        }

        [Test]
        public void should_leave_version_null_without_api_attribute()
        {
            var controller = new UnversionedTestController(_broadcaster.Object);

            controller.Handle(new ModelEvent<TestModel>(new TestModel { Id = 1 }, ModelAction.Updated));

            _messages.Should().ContainSingle().Which.Version.Should().BeNull();
        }

        public class TestModel : ModelBase
        {
        }

        public class TestResource : RestResource
        {
        }

        public abstract class TestControllerBase : RestControllerWithSignalR<TestResource, TestModel>
        {
            protected TestControllerBase(IBroadcastSignalRMessage broadcaster)
                : base(broadcaster)
            {
            }

            public void Batch(IEnumerable<TestResource> resources, int chunkSize)
            {
                BroadcastResourceChangeBatch(ModelAction.Updated, resources, chunkSize);
            }

            protected override TestResource GetResourceById(int id)
            {
                return new TestResource { Id = id };
            }
        }

        [V3ApiController("test")]
        public class V3TestController : TestControllerBase
        {
            public V3TestController(IBroadcastSignalRMessage broadcaster)
                : base(broadcaster)
            {
            }
        }

        [V5ApiController]
        public class V5TestController : TestControllerBase
        {
            public V5TestController(IBroadcastSignalRMessage broadcaster)
                : base(broadcaster)
            {
            }
        }

        public class UnversionedTestController : TestControllerBase
        {
            public UnversionedTestController(IBroadcastSignalRMessage broadcaster)
                : base(broadcaster)
            {
            }
        }
    }
}
