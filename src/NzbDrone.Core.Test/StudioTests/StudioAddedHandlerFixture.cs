using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Movies.Studios.Commands;
using NzbDrone.Core.Movies.Studios.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.StudioTests
{
    [TestFixture]
    public class StudioAddedHandlerFixture : CoreTest<StudioAddedHandler>
    {
        private void GivenMigrationRunning(bool running)
        {
            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(g => g.AutomationPaused)
                  .Returns(running);
        }

        private static List<Studio> Studios()
        {
            return new List<Studio> { new Studio { Id = 1 }, new Studio { Id = 2 } };
        }

        [Test]
        public void should_queue_a_refresh_for_an_added_studio()
        {
            GivenMigrationRunning(false);

            Subject.Handle(new StudioAddedEvent(new Studio { Id = 1 }));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.Is<RefreshStudiosCommand>(c => c.StudioIds.Contains(1)), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void should_queue_a_refresh_per_studio_added_in_bulk()
        {
            GivenMigrationRunning(false);

            Subject.Handle(new StudiosAddedEvent(Studios()));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.PushMany(It.Is<List<RefreshStudiosCommand>>(c => c.Count == 2)), Times.Once());
        }

        [Test]
        public void should_not_queue_refreshes_while_a_migration_runs()
        {
            GivenMigrationRunning(true);

            Subject.Handle(new StudioAddedEvent(new Studio { Id = 1 }));
            Subject.Handle(new StudiosAddedEvent(Studios()));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.IsAny<RefreshStudiosCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.PushMany(It.IsAny<List<RefreshStudiosCommand>>()), Times.Never());
        }
    }
}
