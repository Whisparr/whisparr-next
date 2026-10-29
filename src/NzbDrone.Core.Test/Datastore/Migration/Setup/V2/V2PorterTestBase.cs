using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Dapper;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    // A real v2 SQLite database (schema 221, v2 default rows) as the source, and the v5 test database as the target.
    // The v5 side is SQLite, or PostgreSQL when Whisparr__Postgres__Host is set.
    public abstract class V2PorterTestBase : DbTest
    {
        protected string V2Path { get; private set; }

        protected IDbConnection V5 { get; private set; }

        protected IDbTransaction Transaction { get; private set; }

        protected DatabaseType V5Type { get; private set; }

        protected V2PortContext Context { get; private set; }

        [SetUp]
        public void SetupV2()
        {
            V2Path = Path.Combine(TempFolder, "whisparr2.db");
            LegacyDbBuilder.CreateV2SqliteFromDump(V2Path);

            var mainDatabase = Mocker.Resolve<IMainDatabase>();

            V5 = mainDatabase.OpenConnection();
            V5Type = mainDatabase.DatabaseType;
        }

        [TearDown]
        public void TeardownV2()
        {
            Context?.Dispose();
            Transaction?.Dispose();
            V5?.Dispose();
            Context = null;
            Transaction = null;
            V5 = null;
        }

        protected void InsertV2(string table, object values)
        {
            using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = V2Path, Pooling = false }.ConnectionString);
            connection.Open();

            LegacyDbBuilder.InsertV2Row(connection, table, values);
        }

        protected void ExecuteV2(string sql, object parameters = null)
        {
            using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = V2Path, Pooling = false }.ConnectionString);
            connection.Open();

            connection.Execute(sql, parameters);
        }

        protected V2PortContext OpenContext()
        {
            var install = new LegacyInstall
            {
                Kind = LegacyKind.V2,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = V2Path }
            };

            Transaction = V5.BeginTransaction();
            Context = new V2Porter(new V2CopyPorter(), new V2ConfigPorter(), new V2QualityPorter()).Open(install, V5, Transaction, V5Type);

            return Context;
        }

        protected void Commit()
        {
            Transaction.Commit();
        }

        protected List<LegacyMigrationReportItem> ReportFor(LegacyReportCategory category)
        {
            return Context.Report.Where(r => r.Category == category).ToList();
        }

        protected static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);

            return System.Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
