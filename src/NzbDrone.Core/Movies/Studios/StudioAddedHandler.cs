using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Studios.Commands;
using NzbDrone.Core.Movies.Studios.Events;

namespace NzbDrone.Core.Movies.Studios
{
    public class StudioAddedHandler : IHandle<StudioAddedEvent>, IHandle<StudiosAddedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ILegacyMigrationGate _migrationGate;

        public StudioAddedHandler(IManageCommandQueue commandQueueManager, ILegacyMigrationGate migrationGate)
        {
            _commandQueueManager = commandQueueManager;
            _migrationGate = migrationGate;
        }

        public void Handle(StudioAddedEvent message)
        {
            // The migration fetches each studio, imports its catalog and links its files itself, while a refresh
            // rescans every scene. The daily refresh still picks up a studio added by hand during the migration.
            if (_migrationGate.AutomationPaused)
            {
                return;
            }

            _commandQueueManager.Push(new RefreshStudiosCommand(new List<int> { message.Studio.Id }));
        }

        public void Handle(StudiosAddedEvent message)
        {
            if (_migrationGate.AutomationPaused)
            {
                return;
            }

            _commandQueueManager.PushMany(message.Studios.Select(s => new RefreshStudiosCommand(new List<int> { s.Id })).ToList());
        }
    }
}
