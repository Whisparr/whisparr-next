using System;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.LegacyMigration.Events
{
    public class LegacyMigrationCompletedEvent : IEvent
    {
        // When the migration started; items added since then were added by it
        public DateTime? StartedAt { get; set; }
    }
}
