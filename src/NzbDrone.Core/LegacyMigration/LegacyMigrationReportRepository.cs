using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.LegacyMigration
{
    public interface ILegacyMigrationReportRepository : IBasicRepository<LegacyMigrationReportItem>
    {
        void DeleteBySubject(string subject);
        void DeleteByMovieId(int movieId);
    }

    public class LegacyMigrationReportRepository : BasicRepository<LegacyMigrationReportItem>, ILegacyMigrationReportRepository
    {
        public LegacyMigrationReportRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void DeleteBySubject(string subject)
        {
            Delete(i => i.Subject == subject);
        }

        public void DeleteByMovieId(int movieId)
        {
            Delete(i => i.MovieId == movieId);
        }
    }
}
