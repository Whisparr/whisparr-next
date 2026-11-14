using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Instrumentation
{
    public interface ILogRepository : IBasicRepository<Log>
    {
        void Trim();
    }

    public class LogRepository : BasicRepository<Log>, ILogRepository
    {
        public LogRepository(ILogDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void Trim()
        {
            // No vacuum: a day's trim frees about 1/7 of the file, which the next day's logs reuse
            var trimDate = DateTime.UtcNow.AddDays(-7).Date;
            Delete(c => c.Time <= trimDate);
        }
    }
}
