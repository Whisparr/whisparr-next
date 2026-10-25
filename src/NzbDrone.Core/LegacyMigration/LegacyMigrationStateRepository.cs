using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.LegacyMigration
{
    public interface ILegacyMigrationStateRepository : IBasicRepository<LegacyMigrationState>
    {
        LegacyMigrationState GetCurrent();
    }

    public class LegacyMigrationStateRepository : BasicRepository<LegacyMigrationState>, ILegacyMigrationStateRepository
    {
        public LegacyMigrationStateRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public LegacyMigrationState GetCurrent()
        {
            return All().FirstOrDefault();
        }
    }
}
