using System;
using System.Data;
using System.Data.SQLite;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public static class LegacyDatabaseGuard
    {
        public const long FirstV5Migration = 300;

        // A database with a VersionInfo table but no v5 migration row belongs to Whisparr v2 or v3.
        // A database without a VersionInfo table is empty and safe to migrate.
        public static bool IsLegacy(IDbConnection connection, DatabaseType databaseType)
        {
            if (!HasVersionInfoTable(connection, databaseType))
            {
                return false;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT EXISTS (SELECT 1 FROM \"VersionInfo\" WHERE \"Version\" >= {FirstV5Migration})";

            var hasV5Migration = Convert.ToBoolean(command.ExecuteScalar());

            return !hasV5Migration;
        }

        // A restore candidate is accepted only if it opens as SQLite and isn't a v2/v3 database.
        // It is opened read-only and unpooled so the check neither changes the file nor keeps it locked.
        public static bool IsRestorableSqliteFile(string path)
        {
            var connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = path,
                ReadOnly = true,
                FailIfMissing = true,
                Pooling = false
            }.ConnectionString;

            try
            {
                using var connection = new SQLiteConnection(connectionString);
                connection.Open();

                return !IsLegacy(connection, DatabaseType.SQLite);
            }
            catch (SQLiteException)
            {
                return false;
            }
        }

        private static bool HasVersionInfoTable(IDbConnection connection, DatabaseType databaseType)
        {
            using var command = connection.CreateCommand();

            command.CommandText = databaseType switch
            {
                DatabaseType.SQLite => "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'VersionInfo'",
                DatabaseType.PostgreSQL => "SELECT to_regclass('public.\"VersionInfo\"')::text",
                _ => throw new NotImplementedException($"Unknown database type: {databaseType}")
            };

            var result = command.ExecuteScalar();

            return result != null && result != DBNull.Value;
        }
    }
}
