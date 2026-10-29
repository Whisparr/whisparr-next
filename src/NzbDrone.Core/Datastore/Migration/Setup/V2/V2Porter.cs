using System;
using System.Data;
using Dapper;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    public interface IV2Porter
    {
        V2PortContext Open(LegacyInstall install, IDbConnection v5, IDbTransaction transaction, DatabaseType v5Type);
        void PortSettings(V2PortContext context);
        V2SnapshotCounts SnapshotLibrary(V2PortContext context);
        void WriteReport(V2PortContext context);
    }

    public class V2Porter : IV2Porter
    {
        private readonly V2CopyPorter _copyPorter;
        private readonly V2ConfigPorter _configPorter;

        private readonly V2QualityPorter _qualityPorter;
        private readonly V2ProviderPorter _providerPorter;

        public V2Porter(V2CopyPorter copyPorter, V2ConfigPorter configPorter, V2QualityPorter qualityPorter, V2ProviderPorter providerPorter)
        {
            _copyPorter = copyPorter;
            _configPorter = configPorter;
            _qualityPorter = qualityPorter;
            _providerPorter = providerPorter;
        }

        public V2PortContext Open(LegacyInstall install, IDbConnection v5, IDbTransaction transaction, DatabaseType v5Type)
        {
            var location = install.Database;

            var legacy = location.Type == DatabaseType.PostgreSQL
                ? LegacyConnections.OpenPostgres(location.PostgresConnectionString)
                : LegacyConnections.OpenSqliteReadOnly(location.SqlitePath);

            return new V2PortContext(legacy, v5, transaction, v5Type);
        }

        public void PortSettings(V2PortContext context)
        {
            _copyPorter.Port(context);
            _configPorter.Port(context);
            _qualityPorter.Port(context);
            _providerPorter.Port(context);

            if (context.V5Type == DatabaseType.PostgreSQL)
            {
                PostgresSequences.Reset(context.V5, context.InsertedTables, context.Transaction);
            }
        }

        public V2SnapshotCounts SnapshotLibrary(V2PortContext context)
        {
            throw new NotImplementedException("The library snapshot is added in a later task.");
        }

        public void WriteReport(V2PortContext context)
        {
            foreach (var item in context.Report)
            {
                context.V5.Execute(
                    "INSERT INTO \"LegacyMigrationReportItems\" (\"Category\", \"Area\", \"Subject\", \"Detail\", \"Count\") VALUES (@Category, @Area, @Subject, @Detail, @Count)",
                    new { Category = (int)item.Category, item.Area, item.Subject, item.Detail, item.Count },
                    context.Transaction);
            }
        }
    }
}
