using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.LegacyMigration
{
    public interface ILegacyMigrationReportRepository : IBasicRepository<LegacyMigrationReportItem>
    {
    }

    public class LegacyMigrationReportRepository : BasicRepository<LegacyMigrationReportItem>, ILegacyMigrationReportRepository
    {
        public LegacyMigrationReportRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
