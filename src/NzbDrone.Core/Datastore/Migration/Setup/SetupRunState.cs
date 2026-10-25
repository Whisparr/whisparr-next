using System;
using System.Collections.Generic;
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
        void RecordSqlitePartial(string path);
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

        public void RecordSqlitePartial(string path)
        {
            var sidecar = Load() ?? new Sidecar();

            if (!sidecar.SqlitePartials.Contains(path))
            {
                sidecar.SqlitePartials.Add(path);
            }

            Save(sidecar);
        }

        public void Complete()
        {
            File.Delete(SidecarPath);
        }

        public bool DiscardIfPresent()
        {
            var sidecar = Load();

            if (sidecar == null)
            {
                return false;
            }

            var discarded = false;

            foreach (var database in sidecar.Databases)
            {
                discarded |= DropDatabase(database);
            }

            foreach (var path in sidecar.SqlitePartials.Where(File.Exists))
            {
                File.Delete(path);
                _logger.Info("Discarded partial database {0} from an interrupted setup", path);
                discarded = true;
            }

            var config5Path = _appFolderInfo.GetConfigPath();

            if (sidecar.SeededConfig5 && File.Exists(config5Path))
            {
                File.Delete(config5Path);
                _logger.Info("Discarded {0} created by an interrupted setup", config5Path);
                discarded = true;
            }

            File.Delete(SidecarPath);

            return discarded;
        }

        private bool DropDatabase(RecordedDatabase database)
        {
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
                _logger.Warn(ex, "Could not drop database {0} from an interrupted setup", database.Name);

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
            public List<RecordedDatabase> Databases { get; set; } = new();
            public List<string> SqlitePartials { get; set; } = new();
        }

        private sealed class RecordedDatabase
        {
            public string Name { get; set; }
            public string Server { get; set; }
        }
    }
}
