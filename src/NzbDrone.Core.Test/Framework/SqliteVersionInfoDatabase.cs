using System.Data.SQLite;
using Dapper;

namespace NzbDrone.Core.Test.Framework
{
    // A bare SQLite file holding only a FluentMigrator-shaped VersionInfo table, for tests that inspect a database file
    public static class SqliteVersionInfoDatabase
    {
        public static void Create(string path, params long[] versions)
        {
            var connectionString = new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString;

            using var connection = new SQLiteConnection(connectionString);
            connection.Open();

            connection.Execute("CREATE TABLE \"VersionInfo\" (\"Version\" INTEGER NOT NULL, \"AppliedOn\" DATETIME, \"Description\" TEXT)");

            foreach (var version in versions)
            {
                // FluentMigrator records the migration class name as the description
                var description = version == 300 ? "whisparr_v5" : $"migration_{version}";

                connection.Execute("INSERT INTO \"VersionInfo\" (\"Version\", \"AppliedOn\", \"Description\") VALUES (@version, CURRENT_TIMESTAMP, @description)",
                    new { version, description });
            }
        }
    }
}
