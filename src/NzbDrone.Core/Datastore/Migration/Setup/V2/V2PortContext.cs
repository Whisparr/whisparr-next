using System;
using System.Collections.Generic;
using System.Data;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    // Everything one v2 port run shares: the read-only legacy connection, the v5 transaction and the report so far.
    public sealed class V2PortContext : IDisposable
    {
        public V2PortContext(IDbConnection legacy, IDbConnection v5, IDbTransaction transaction, DatabaseType v5Type)
        {
            Legacy = legacy;
            V5 = v5;
            Transaction = transaction;
            V5Type = v5Type;
        }

        // Owned: disposed with the context.
        public IDbConnection Legacy { get; }

        public IDbConnection V5 { get; }

        public IDbTransaction Transaction { get; }

        public DatabaseType V5Type { get; }

        public List<LegacyMigrationReportItem> Report { get; } = new();

        // Tables written with explicit Ids, so the PostgreSQL sequences can be reset afterwards.
        public HashSet<string> InsertedTables { get; } = new(StringComparer.Ordinal);

        public void Add(LegacyReportCategory category, string area, string subject, string detail, int count = 1)
        {
            Report.Add(new LegacyMigrationReportItem
            {
                Category = category,
                Area = area,
                Subject = subject,
                Detail = detail,
                Count = count
            });
        }

        public void Dispose()
        {
            Legacy?.Dispose();
        }
    }
}
