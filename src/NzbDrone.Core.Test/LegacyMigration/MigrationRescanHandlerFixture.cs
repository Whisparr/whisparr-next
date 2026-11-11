using Moq;
using NUnit.Framework;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.LegacyMigration.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class MigrationRescanHandlerFixture : CoreTest<MigrationRescanHandler>
    {
        [Test]
        public void queues_one_library_rescan_when_migration_completes()
        {
            Subject.Handle(new LegacyMigrationCompletedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(
                      v => v.Push(
                          It.Is<RescanMovieCommand>(c => c.MovieId == null),
                          It.IsAny<CommandPriority>(),
                          It.IsAny<CommandTrigger>()),
                      Times.Once());
        }
    }
}
