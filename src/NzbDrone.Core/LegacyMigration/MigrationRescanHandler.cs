using NzbDrone.Core.LegacyMigration.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.LegacyMigration
{
    // The per-scene scan is skipped during a migration, so scan the whole library once afterwards.
    public class MigrationRescanHandler : IHandle<LegacyMigrationCompletedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;

        public MigrationRescanHandler(IManageCommandQueue commandQueueManager)
        {
            _commandQueueManager = commandQueueManager;
        }

        public void Handle(LegacyMigrationCompletedEvent message)
        {
            _commandQueueManager.Push(new RescanMovieCommand());
        }
    }
}
