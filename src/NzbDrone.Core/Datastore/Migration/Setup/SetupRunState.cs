using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Dapper;
using NLog;
using Npgsql;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public interface ISetupRunState
    {
        void Begin(bool seededConfig5);
        void RecordDatabase(string name);

        // A pre-existing empty Postgres database that phase 1 writes into. Discard empties it again and never drops it.
        void RecordReusedEmptyTarget(string name);
        void RecordSqlitePartial(string path);

        // A copy of a config5.xml the wizard did not create, taken before it is overwritten. Discard moves it back,
        // Complete deletes it.
        void RecordConfig5Backup(string path);
        void Complete();
        bool DiscardIfPresent();
    }

    // Remembers what a phase 1 run created, in <AppData>/v5-setup.json, so an interrupted run can be undone
    // on the next start and nothing else is touched. It is built by hand in Bootstrap, before the container
    // exists. The sidecar holds the Postgres server credentials, as config.xml already does.
    public class SetupRunState : ISetupRunState
    {
        private const string SidecarFile = "v5-setup.json";
        private const string MaintenanceDatabase = "postgres";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public SetupRunState(IAppFolderInfo appFolderInfo)
        {
            _appFolderInfo = appFolderInfo;
            _logger = NzbDroneLogger.GetLogger(this);
        }

        // The Postgres server to record for later cleanup. Defaults to the environment-bound options.
        public PostgresOptions PostgresOptions { get; init; }

        private string SidecarPath => Path.Combine(_appFolderInfo.GetAppDataPath(), SidecarFile);

        public void Begin(bool seededConfig5)
        {
            Save(new Sidecar { SeededConfig5 = seededConfig5 });
        }

        public void RecordDatabase(string name)
        {
            var sidecar = Load() ?? new Sidecar();

            if (sidecar.Databases.Any(d => d.Name == name))
            {
                return;
            }

            sidecar.Databases.Add(new RecordedDatabase { Name = name, Server = ServerConnectionString() });
            Save(sidecar);
        }

        public void RecordReusedEmptyTarget(string name)
        {
            var sidecar = Load() ?? new Sidecar();

            if (sidecar.ReusedEmptyDatabases.Any(d => d.Name == name))
            {
                return;
            }

            sidecar.ReusedEmptyDatabases.Add(new RecordedDatabase { Name = name, Server = ServerConnectionString() });
            Save(sidecar);
        }

        public void RecordSqlitePartial(string path)
        {
            var sidecar = Load() ?? new Sidecar();

            if (!sidecar.SqlitePartials.Contains(path))
            {
                sidecar.SqlitePartials.Add(path);
            }

            Save(sidecar);
        }

        public void RecordConfig5Backup(string path)
        {
            var sidecar = Load() ?? new Sidecar();

            sidecar.Config5Backup = path;
            Save(sidecar);
        }

        public void Complete()
        {
            DeleteConfig5Backup(Load());
            File.Delete(SidecarPath);
        }

        private static void DeleteConfig5Backup(Sidecar sidecar)
        {
            if (sidecar?.Config5Backup != null)
            {
                File.Delete(sidecar.Config5Backup);
            }
        }

        public bool DiscardIfPresent()
        {
            var sidecar = Load();

            if (sidecar == null)
            {
                return false;
            }

            // A run that finished but left its sidecar behind (Complete threw) must not undo the database it built.
            if (IsFinishedV5(sidecar))
            {
                _logger.Warn("{0} was left behind by a setup that finished, so nothing is discarded", SidecarPath);
                DeleteConfig5Backup(sidecar);
                File.Delete(SidecarPath);

                return false;
            }

            var discarded = false;

            var failed = new List<RecordedDatabase>();

            foreach (var database in sidecar.Databases)
            {
                discarded |= DropDatabase(database, out var dropFailed);

                if (dropFailed)
                {
                    failed.Add(database);
                }
            }

            var failedReused = new List<RecordedDatabase>();

            foreach (var database in sidecar.ReusedEmptyDatabases)
            {
                discarded |= EmptyDatabase(database, out var emptyFailed);

                if (emptyFailed)
                {
                    failedReused.Add(database);
                }
            }

            foreach (var path in sidecar.SqlitePartials.Where(File.Exists))
            {
                File.Delete(path);
                _logger.Info("Discarded partial database {0} from an interrupted setup", path);
                discarded = true;
            }

            var config5Path = _appFolderInfo.GetConfigPath();

            if (sidecar.Config5Backup != null && File.Exists(sidecar.Config5Backup))
            {
                // The user's own file, which setup overwrote.
                File.Move(sidecar.Config5Backup, config5Path, true);
                _logger.Info("Restored {0} from before an interrupted setup", config5Path);
                discarded = true;
            }
            else if (sidecar.SeededConfig5 && File.Exists(config5Path))
            {
                File.Delete(config5Path);
                _logger.Info("Discarded {0} created by an interrupted setup", config5Path);
                discarded = true;
            }

            if (failed.Count > 0 || failedReused.Count > 0)
            {
                // Keep the sidecar, with only the databases still to drop or empty, so the next start tries again
                // instead of orphaning them. Everything else it recorded is already gone.
                Save(new Sidecar { Databases = failed, ReusedEmptyDatabases = failedReused });

                return true;
            }

            File.Delete(SidecarPath);

            return discarded;
        }

        // True when a v5 database this run built already holds its LegacyMigrationState row, which the finisher writes
        // in the same transaction that completes the build.
        private bool IsFinishedV5(Sidecar sidecar)
        {
            foreach (var partial in sidecar.SqlitePartials)
            {
                var final = Path.Combine(Path.GetDirectoryName(partial), "whisparr5.db");

                if (File.Exists(final) && HasStateRow(() => new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = final, ReadOnly = true, Pooling = false }.ConnectionString), "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'LegacyMigrationState'"))
                {
                    return true;
                }
            }

            return sidecar.Databases.Concat(sidecar.ReusedEmptyDatabases).Any(d =>
                HasStateRow(
                    () => new NpgsqlConnection(new NpgsqlConnectionStringBuilder(d.Server) { Database = d.Name, Pooling = false, Enlist = false }.ConnectionString),
                    "SELECT to_regclass('\"LegacyMigrationState\"')::text"));
        }

        private bool HasStateRow(Func<System.Data.IDbConnection> open, string tableCheck)
        {
            try
            {
                using var connection = open();
                connection.Open();

                return connection.ExecuteScalar<string>(tableCheck) != null &&
                       connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"LegacyMigrationState\"") > 0;
            }
            catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
            {
                _logger.Debug(ex, "Could not check whether a recorded database is a finished v5 database");

                return false;
            }
        }

        private bool DropDatabase(RecordedDatabase database, out bool failed)
        {
            failed = false;

            try
            {
                var server = new NpgsqlConnectionStringBuilder(database.Server)
                {
                    Database = MaintenanceDatabase,
                    Pooling = false,
                    Enlist = false
                };

                using var connection = new NpgsqlConnection(server.ConnectionString);
                connection.Open();

                // The name was recorded because this run created the database, so it is quoted and dropped alone.
                var quoted = "\"" + database.Name.Replace("\"", "\"\"") + "\"";
                var existed = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pg_database WHERE datname = @name", new { name = database.Name }) > 0;

                if (!existed)
                {
                    return false;
                }

#pragma warning disable S2077 // A database name cannot be a parameter, and it is quoted above.
                connection.Execute($"DROP DATABASE {quoted} WITH (FORCE)");
#pragma warning restore S2077
                _logger.Info("Dropped database {0} from an interrupted setup", database.Name);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not drop database {0} from an interrupted setup, it stays recorded and is retried on the next start", database.Name);
                failed = true;

                return false;
            }
        }

        // The database existed and was empty before this run, so it goes back to empty, never dropped.
        private bool EmptyDatabase(RecordedDatabase database, out bool failed)
        {
            failed = false;

            try
            {
                var target = new NpgsqlConnectionStringBuilder(database.Server)
                {
                    Database = database.Name,
                    Pooling = false,
                    Enlist = false
                };

                using var connection = new NpgsqlConnection(target.ConnectionString);

                try
                {
                    connection.Open();
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
                {
                    return false;
                }

                var hadObjects = connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public'") > 0;

                // The pre-15 default lets everyone create in public, 15 and later only grants usage.
                var grant = connection.ExecuteScalar<int>("SELECT current_setting('server_version_num')::int") >= 150000
                    ? "GRANT USAGE ON SCHEMA public TO PUBLIC"
                    : "GRANT ALL ON SCHEMA public TO PUBLIC";

                using var transaction = connection.BeginTransaction();
                connection.Execute("DROP SCHEMA public CASCADE", transaction: transaction);
                connection.Execute("CREATE SCHEMA public", transaction: transaction);
                connection.Execute(grant, transaction: transaction);
                transaction.Commit();

                if (hadObjects)
                {
                    _logger.Info("Emptied database {0} from an interrupted setup", database.Name);
                }

                return hadObjects;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not empty database {0} from an interrupted setup, it stays recorded and is retried on the next start", database.Name);
                failed = true;

                return false;
            }
        }

        private string ServerConnectionString()
        {
            var options = PostgresOptions ?? PostgresOptions.GetOptions();
            var config5 = ReadConfig5();

            var connectionString = First(options.MainDbConnectionString, config5.GetValueOrDefault("PostgresMainDbConnectionString"));

            if (connectionString != null)
            {
                return new NpgsqlConnectionStringBuilder(connectionString).ConnectionString;
            }

            var port = options.Port;

            if (port == 0 && !int.TryParse(config5.GetValueOrDefault("PostgresPort"), out port))
            {
                port = 5432;
            }

            return new NpgsqlConnectionStringBuilder
            {
                Host = First(options.Host, config5.GetValueOrDefault("PostgresHost")),
                Port = port,
                Username = First(options.User, config5.GetValueOrDefault("PostgresUser")),
                Password = First(options.Password, config5.GetValueOrDefault("PostgresPassword"))
            }.ConnectionString;
        }

        private Dictionary<string, string> ReadConfig5()
        {
            try
            {
                var path = _appFolderInfo.GetConfigPath();

                return File.Exists(path)
                    ? XDocument.Load(path).Root?.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value) ?? new Dictionary<string, string>()
                    : new Dictionary<string, string>();
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or ArgumentException)
            {
                return new Dictionary<string, string>();
            }
        }

        private static string First(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        private Sidecar Load()
        {
            if (!File.Exists(SidecarPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(SidecarPath), JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.Warn(ex, "{0} is unreadable, so it is ignored", SidecarPath);

                return null;
            }
        }

        private void Save(Sidecar sidecar)
        {
            File.WriteAllText(SidecarPath, JsonSerializer.Serialize(sidecar, JsonOptions));
        }

        private sealed class Sidecar
        {
            public bool SeededConfig5 { get; set; }
            public string Config5Backup { get; set; }
            public List<RecordedDatabase> Databases { get; set; } = new();
            public List<RecordedDatabase> ReusedEmptyDatabases { get; set; } = new();
            public List<string> SqlitePartials { get; set; } = new();
        }

        private sealed class RecordedDatabase
        {
            public string Name { get; set; }
            public string Server { get; set; }
        }
    }
}
