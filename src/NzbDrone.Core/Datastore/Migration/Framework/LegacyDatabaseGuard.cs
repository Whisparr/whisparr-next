using System;
using System.Data;
using System.Data.SQLite;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public static class LegacyDatabaseGuard
    {
        public const long FirstV5Migration = 300;

        // FluentMigrator records a migration's class name as its VersionInfo description.
        private const string FirstV5MigrationDescription = nameof(whisparr_v5);

        // A database with a VersionInfo table but no v5 marker row belongs to Whisparr v2 or v3.
        // A database without a VersionInfo table is empty and safe to migrate.
        public static bool IsLegacy(IDbConnection connection, DatabaseType databaseType)
        {
            var versionTable = FindVersionInfoTable(connection, databaseType);

            if (versionTable == null)
            {
                return false;
            }

            using var command = connection.CreateCommand();

            // versionTable is a quoted identifier produced by the database, not user input.
            command.CommandText = $"SELECT EXISTS (SELECT 1 FROM {versionTable} WHERE \"Version\" = @version AND \"Description\" = @description)";
            AddParameter(command, "version", FirstV5Migration);
            AddParameter(command, "description", FirstV5MigrationDescription);

            var hasV5Marker = Convert.ToBoolean(command.ExecuteScalar());

            return !hasV5Marker;
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

        // Returns the VersionInfo table as a quoted SQL name, or null when there is none. On PostgreSQL the
        // search_path lookup comes first because FluentMigrator reads VersionInfo unqualified, and the row
        // query uses the same resolved table, so the guard checks exactly the table migrations would use.
        private static string FindVersionInfoTable(IDbConnection connection, DatabaseType databaseType)
        {
            using var command = connection.CreateCommand();

            switch (databaseType)
            {
                case DatabaseType.SQLite:
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'VersionInfo'";

                    return command.ExecuteScalar() is string ? "\"VersionInfo\"" : null;
                case DatabaseType.PostgreSQL:
                    command.CommandText = "SELECT COALESCE(to_regclass('\"VersionInfo\"'), to_regclass('public.\"VersionInfo\"'))::text";

                    return command.ExecuteScalar() as string;
                default:
                    throw new NotImplementedException($"Unknown database type: {databaseType}");
            }
        }

        private static void AddParameter(IDbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
