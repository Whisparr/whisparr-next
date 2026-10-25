using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using Dapper;
using Npgsql;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // Every connection to a legacy database is read-only, so detection and migration can never change it.
    public static class LegacyConnections
    {
        // Read-only and unpooled, so opening neither changes the journal mode nor keeps the file locked.
        // immutable=1 stops SQLite creating -shm/-wal files next to a WAL-mode database. The legacy app is
        // stopped while we read, so there are no uncheckpointed writes to miss.
        public static IDbConnection OpenSqliteReadOnly(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Database file not found", path);
            }

            var connectionString = new SQLiteConnectionStringBuilder
            {
                FullUri = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?immutable=1",
                ReadOnly = true,
                FailIfMissing = true,
                Pooling = false
            }.ConnectionString;

            var connection = new SQLiteConnection(connectionString);

            try
            {
                connection.Open();
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            return connection;
        }

        public static IDbConnection OpenPostgres(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Enlist = false,
                Pooling = false
            };

            var connection = new NpgsqlConnection(builder.ConnectionString);

            try
            {
                connection.Open();
                connection.Execute("SET default_transaction_read_only = on");
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            return connection;
        }
    }
}
