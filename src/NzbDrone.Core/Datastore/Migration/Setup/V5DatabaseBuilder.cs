using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using Npgsql;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public class BuiltDatabases
    {
        public string MainConnectionString { get; set; }
        public string LogConnectionString { get; set; }
        public DatabaseType Type { get; set; }

        // whisparr5.db.partial for SQLite, null for PostgreSQL.
        public string SqlitePartialPath { get; set; }

        public List<string> CreatedPostgresDatabases { get; set; } = new();
    }

    public interface IV5DatabaseBuilder
    {
        // recordCreated is called with each Postgres database name right after it is created and before
        // anything is written to it, so the caller can persist it for cleanup.
        //
        // recordReused is called with each pre-existing empty Postgres database the build accepts, before
        // anything is written to it, so a failed run can return it to empty instead of leaving it non-empty.
        BuiltDatabases CopyEros(LegacyInstall install, V5DatabaseTargets targets, Action<string> recordCreated, Action<string> recordReused);
    }

    public class V5DatabaseBuilder : IV5DatabaseBuilder
    {
        public const string PartialFileName = "whisparr5.db.partial";
        public const string SqliteLogFileName = "logs5.db";

        private const string InsufficientPrivilege = "42501";
        private const string VersionInfoTable = "VersionInfo";

        private readonly IMigrationController _migrationController;
        private readonly PostgresTableCopier _tableCopier;

        public V5DatabaseBuilder(IMigrationController migrationController, PostgresTableCopier tableCopier)
        {
            _migrationController = migrationController;
            _tableCopier = tableCopier;
        }

        public BuiltDatabases CopyEros(LegacyInstall install, V5DatabaseTargets targets, Action<string> recordCreated, Action<string> recordReused)
        {
            try
            {
                return install.Database.Type == DatabaseType.PostgreSQL
                    ? CopyPostgres(install, targets, recordCreated, recordReused)
                    : CopySqlite(install);
            }
            catch (SetupStepException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SetupStepException("The v5 database could not be built from your existing database. Your existing database has not been changed. " + ex.Message, ex);
            }
        }

        private BuiltDatabases CopySqlite(LegacyInstall install)
        {
            var legacyPath = install.Database.SqlitePath;
            var directory = Path.GetDirectoryName(legacyPath);
            var partial = Path.Combine(directory, PartialFileName);

            DeletePartial(partial);

            try
            {
                using (var snapshot = LegacySqliteSnapshot.Open(legacyPath, Path.Combine(Path.GetTempPath(), "whisparr-v5-snapshot")))
                {
                    var partialConnectionString = SqliteConnectionString(partial, SQLiteJournalModeEnum.Truncate);

                    using var target = new SQLiteConnection(partialConnectionString);
                    target.Open();

                    ((SQLiteConnection)snapshot.Connection).BackupDatabase(target, "main", "main", -1, null, 500);
                }

                var migrationConnectionString = SqliteConnectionString(partial, SQLiteJournalModeEnum.Default);

                _migrationController.Migrate(
                    migrationConnectionString,
                    new MigrationContext(MigrationType.Main) { AllowLegacySource = true },
                    DatabaseType.SQLite);

                return new BuiltDatabases
                {
                    Type = DatabaseType.SQLite,
                    SqlitePartialPath = partial,
                    MainConnectionString = migrationConnectionString,
                    LogConnectionString = SqliteConnectionString(Path.Combine(directory, SqliteLogFileName), SQLiteJournalModeEnum.Default)
                };
            }
            catch
            {
                DeletePartial(partial);
                throw;
            }
        }

        private BuiltDatabases CopyPostgres(LegacyInstall install, V5DatabaseTargets targets, Action<string> recordCreated, Action<string> recordReused)
        {
            var legacyBuilder = new NpgsqlConnectionStringBuilder(install.Database.PostgresConnectionString) { Enlist = false };
            var legacyMain = install.Database.PostgresMainDb ?? legacyBuilder.Database;

            ValidateTargets(targets, legacyMain);

            var result = new BuiltDatabases { Type = DatabaseType.PostgreSQL };
            bool streamed;

            using (var admin = OpenAdmin(legacyBuilder))
            {
                var reusedMain = RequireEmptyOrMissing(legacyBuilder, admin, targets.MainDb);
                var reusedLog = RequireEmptyOrMissing(legacyBuilder, admin, targets.LogDb);

                // Both are acceptable, so record them before anything is written to either.
                if (reusedMain)
                {
                    recordReused(targets.MainDb);
                }

                if (reusedLog)
                {
                    recordReused(targets.LogDb);
                }

                var mainExisted = DatabaseExists(admin, targets.MainDb);
                streamed = true;

                if (!mainExisted)
                {
                    // Pooled app connections would keep the legacy database busy and make TEMPLATE fail.
                    NpgsqlConnection.ClearAllPools();

                    streamed = !TryCreate(admin, targets, result, targets.MainDb, legacyMain, recordCreated);
                }

                if (!DatabaseExists(admin, targets.LogDb))
                {
                    TryCreate(admin, targets, result, targets.LogDb, null, recordCreated);
                }
            }

            var mainConnectionString = ConnectionStringFor(legacyBuilder, targets.MainDb);

            if (streamed)
            {
                _migrationController.Migrate(
                    mainConnectionString,
                    new MigrationContext(MigrationType.Main, install.MaxMigration) { AllowLegacySource = true },
                    DatabaseType.PostgreSQL);

                StreamTables(install.Database.PostgresConnectionString, mainConnectionString);
            }

            _migrationController.Migrate(
                mainConnectionString,
                new MigrationContext(MigrationType.Main) { AllowLegacySource = true },
                DatabaseType.PostgreSQL);

            result.MainConnectionString = mainConnectionString;
            result.LogConnectionString = ConnectionStringFor(legacyBuilder, targets.LogDb);

            return result;
        }

        private static void ValidateTargets(V5DatabaseTargets targets, string legacyMain)
        {
            if (string.IsNullOrWhiteSpace(targets.MainDb) || string.IsNullOrWhiteSpace(targets.LogDb))
            {
                throw new SetupStepException("The v5 database names are empty. Choose a name for the main and the log database.");
            }

            var legacy = new[] { legacyMain, Config5Seeder.LegacyLogDb };

            foreach (var target in new[] { targets.MainDb, targets.LogDb })
            {
                if (legacy.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    throw new SetupStepException($"The v5 database name \"{target}\" is the same as an existing database. Choose a different name so your existing database is left untouched.");
                }
            }

            if (string.Equals(targets.MainDb, targets.LogDb, StringComparison.OrdinalIgnoreCase))
            {
                throw new SetupStepException("The main and the log database must have different names.");
            }
        }

        private static NpgsqlConnection OpenAdmin(NpgsqlConnectionStringBuilder legacyBuilder)
        {
            var admin = new NpgsqlConnectionStringBuilder(legacyBuilder.ConnectionString)
            {
                Database = "postgres",
                Pooling = false,
                Enlist = false
            };

            var connection = new NpgsqlConnection(admin.ConnectionString);

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

        private static bool DatabaseExists(NpgsqlConnection admin, string name)
        {
            return admin.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", new { name });
        }

        // Returns true when the database already exists and is empty.
        private bool RequireEmptyOrMissing(NpgsqlConnectionStringBuilder legacyBuilder, NpgsqlConnection admin, string name)
        {
            if (!DatabaseExists(admin, name))
            {
                return false;
            }

            var builder = new NpgsqlConnectionStringBuilder(ConnectionStringFor(legacyBuilder, name)) { Pooling = false };

            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();

            if (_tableCopier.ListTables(connection).Any())
            {
                throw new SetupStepException($"The database \"{name}\" already exists and is not empty. Choose a different name or empty it first.");
            }

            return true;
        }

        // Returns true when the database was created from the legacy template.
        private bool TryCreate(NpgsqlConnection admin, V5DatabaseTargets targets, BuiltDatabases result, string name, string template, Action<string> recordCreated)
        {
            if (template != null)
            {
                try
                {
                    admin.Execute($"CREATE DATABASE {Quote(name)} TEMPLATE {Quote(template)}");
                    recordCreated(name);
                    result.CreatedPostgresDatabases.Add(name);

                    return true;
                }
                catch (PostgresException)
                {
                    // Busy template (other sessions) or no privilege: try a plain create below.
                }
            }

            try
            {
                admin.Execute($"CREATE DATABASE {Quote(name)}");
                recordCreated(name);
                result.CreatedPostgresDatabases.Add(name);

                return false;
            }
            catch (PostgresException ex) when (ex.SqlState == InsufficientPrivilege)
            {
                throw new SetupStepException(DeniedMessage(targets), ex);
            }
        }

        private static string DeniedMessage(V5DatabaseTargets targets)
        {
            return "The database user is not allowed to create databases. Ask a database administrator to run:\n" +
                   $"CREATE DATABASE {Quote(targets.MainDb)};\n" +
                   $"CREATE DATABASE {Quote(targets.LogDb)};\n" +
                   "then set the environment variables " +
                   $"Whisparr__Postgres__MainDb={targets.MainDb} and Whisparr__Postgres__LogDb={targets.LogDb} and restart Whisparr.";
        }

        private void StreamTables(string legacyConnectionString, string targetConnectionString)
        {
            using var source = (NpgsqlConnection)LegacyConnections.OpenPostgres(legacyConnectionString);
            using var target = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(targetConnectionString) { Pooling = false, Enlist = false }.ConnectionString);
            target.Open();

            var tables = _tableCopier.ListTables(source)
                .Where(t => !string.Equals(t, VersionInfoTable, StringComparison.Ordinal))
                .ToList();

            // Migrations seed a few rows, so the copy replaces them.
            target.Execute("TRUNCATE " + string.Join(", ", tables.Select(Quote)));

            var temp = Path.Combine(Path.GetTempPath(), "whisparr-v5-copy-" + Path.GetRandomFileName());

            try
            {
                foreach (var table in tables)
                {
                    using (var output = File.Create(temp))
                    {
                        _tableCopier.CopyOut(source, table, output);
                    }

                    using (var input = File.OpenRead(temp))
                    {
                        _tableCopier.CopyIn(target, table, input);
                    }
                }
            }
            finally
            {
                File.Delete(temp);
            }

            foreach (var table in tables)
            {
                var sequence = target.ExecuteScalar<string>("SELECT pg_get_serial_sequence(@table, 'Id')", new { table = Quote(table) });

                if (sequence != null)
                {
                    target.Execute($"SELECT setval(@sequence, COALESCE((SELECT MAX(\"Id\") FROM {Quote(table)}), 1), (SELECT MAX(\"Id\") FROM {Quote(table)}) IS NOT NULL)", new { sequence });
                }
            }
        }

        private static string ConnectionStringFor(NpgsqlConnectionStringBuilder legacyBuilder, string database)
        {
            return new NpgsqlConnectionStringBuilder(legacyBuilder.ConnectionString)
            {
                Database = database,
                Enlist = false
            }.ConnectionString;
        }

        private static string SqliteConnectionString(string path, SQLiteJournalModeEnum journalMode)
        {
            return new SQLiteConnectionStringBuilder
            {
                DataSource = path,
                JournalMode = journalMode,
                Pooling = false
            }.ConnectionString;
        }

        private static void DeletePartial(string partial)
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                try
                {
                    File.Delete(partial + suffix);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort cleanup.
                }
            }
        }

        private static string Quote(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
