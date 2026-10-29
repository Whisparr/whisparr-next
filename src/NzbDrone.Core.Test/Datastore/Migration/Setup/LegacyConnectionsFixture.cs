using System;
using Dapper;
using FluentAssertions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class LegacyConnectionsFixture : TestBase
    {
        private string ConnectionString()
        {
            var host = Environment.GetEnvironmentVariable("Whisparr__Postgres__Host");

            if (string.IsNullOrWhiteSpace(host))
            {
                Assert.Ignore("Whisparr__Postgres__Host is not set");
            }

            return new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.TryParse(Environment.GetEnvironmentVariable("Whisparr__Postgres__Port"), out var port) ? port : 5432,
                Username = Environment.GetEnvironmentVariable("Whisparr__Postgres__User"),
                Password = Environment.GetEnvironmentVariable("Whisparr__Postgres__Password"),
                Database = "postgres"
            }.ConnectionString;
        }

        [Test]
        public void a_legacy_postgres_session_should_refuse_writes()
        {
            var connectionString = ConnectionString();
            var table = "legacy_ro_probe_" + Guid.NewGuid().ToString("N");

            try
            {
                using var legacy = LegacyConnections.OpenPostgres(connectionString);

                // 25006 is read_only_sql_transaction, which is how the server enforces "legacy data is only read".
                var ex = Assert.Throws<PostgresException>(() => legacy.Execute($"CREATE TABLE \"{table}\" (\"Id\" integer)"));

                ex.SqlState.Should().Be("25006");

                legacy.ExecuteScalar<int>("SELECT 1").Should().Be(1);
            }
            finally
            {
                using var cleanup = new NpgsqlConnection(connectionString);
                cleanup.Open();
                cleanup.Execute($"DROP TABLE IF EXISTS \"{table}\"");
            }
        }
    }
}
