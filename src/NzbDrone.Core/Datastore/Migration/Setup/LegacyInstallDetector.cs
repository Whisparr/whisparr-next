using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Dapper;
using Microsoft.Extensions.Options;
using NLog;
using Npgsql;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public interface ILegacyInstallDetector
    {
        DetectionResult Detect();
    }

    // Looks for Whisparr v2 and Eros installs that a v5 start would otherwise ignore. Everything it opens is
    // read-only. It is built by hand in Bootstrap, before the container exists.
    public class LegacyInstallDetector : ILegacyInstallDetector
    {
        private const string LegacyConfigFile = "config.xml";
        private const string V5SqliteFile = "whisparr5.db";
        private const string LegacyPostgresMainDb = "whisparr-main";
        private const string V5PostgresMainDb = "whisparr5-main";
        private const long FirstSupportedV2Migration = 221;

        private static readonly Lazy<HashSet<long>> KnownMigrations = new(LoadKnownMigrations);

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly PostgresOptions _options;
        private readonly Logger _logger;

        public LegacyInstallDetector(IAppFolderInfo appFolderInfo, IOptions<PostgresOptions> postgresOptions)
        {
            _appFolderInfo = appFolderInfo;
            _options = postgresOptions?.Value ?? new PostgresOptions();
            _logger = NzbDroneLogger.GetLogger(this);
        }

        public DetectionResult Detect()
        {
            var appData = _appFolderInfo.GetAppDataPath();
            var legacyConfigPath = Path.Combine(appData, LegacyConfigFile);
            var legacyConfig = ReadConfig(legacyConfigPath);
            var v5ConfigPath = _appFolderInfo.GetConfigPath();
            var v5ConfigExists = File.Exists(v5ConfigPath);

            var result = new DetectionResult { LegacyConfigPath = legacyConfigPath };
            var legacyPostgres = ResolvePostgres(legacyConfig, LegacyPostgresMainDb);

            var v5Config = v5ConfigExists ? ReadConfig(v5ConfigPath) : null;

            // A finished migration whose environment still points at the legacy database would otherwise look like a
            // fresh legacy install, and offer the migration again.
            if (v5Config != null && DetectPendingEnvSwitch(legacyConfig, v5Config, result))
            {
                return result;
            }

            if (HasMarkedV5Database(appData, legacyPostgres, v5Config))
            {
                return result;
            }

            var installs = new List<LegacyInstall>();

            if (legacyPostgres.IsConfigured)
            {
                TryAdd(installs, result.Errors, DescribePostgres(legacyPostgres), () => DetectPostgres(legacyPostgres, legacyConfig), true);
            }

            foreach (var name in new[] { "whisparr2.db", "whisparr3.db" })
            {
                var path = Path.Combine(appData, name);

                if (File.Exists(path))
                {
                    TryAdd(installs, result.Errors, path, () => DetectSqlite(path, legacyConfig), false);
                }
            }

            // One install per kind: a configured Postgres database comes first and shadows stale SQLite files.
            result.Installs = installs.GroupBy(i => i.Kind).Select(g => g.First()).ToList();

            // A legacy database that exists but can't be read must not fall through to a fresh v5 boot.
            result.SetupModeRequired = result.Installs.Count > 0 || result.Errors.Count > 0;

            return result;
        }

        // Read-only. Probes the v5 main database under the names config5.xml gives it, ignoring the environment.
        private bool DetectPendingEnvSwitch(Dictionary<string, string> legacyConfig, Dictionary<string, string> v5Config, DetectionResult result)
        {
            var v5Postgres = ResolvePostgres(v5Config, V5PostgresMainDb, true);

            if (!v5Postgres.IsConfigured)
            {
                return false;
            }

            string FromLegacy(string key) => legacyConfig.GetValueOrDefault(key);

            var v5Main = new NpgsqlConnectionStringBuilder(v5Postgres.ConnectionString).Database;
            var v5Log = DatabaseOf(v5Config.GetValueOrDefault("PostgresLogDbConnectionString")) ??
                        (v5Config.GetValueOrDefault("PostgresLogDb").IsNotNullOrWhiteSpace() ? v5Config["PostgresLogDb"] : Config5Seeder.V5LogDb);

            var envMain = Blank(_options.MainDb);
            var envLog = Blank(_options.LogDb);
            var envMainConnectionString = Blank(_options.MainDbConnectionString);
            var envLogConnectionString = Blank(_options.LogDbConnectionString);

            var offenders = Config5Seeder.EnvVarsNamingLegacy(
                Config5Seeder.LegacyMainName(FromLegacy, null),
                Config5Seeder.LegacyLogName(FromLegacy),
                envMain,
                envLog,
                envMainConnectionString,
                envLogConnectionString);

            // A legacy database that only the environment named has no trace in config.xml, so look at what the
            // variable points to.
            if (!offenders.Contains(Config5Seeder.MainDbVariable) && envMain != null && envMain != v5Main && IsLegacyDatabase(v5Postgres, envMain))
            {
                offenders.Add(Config5Seeder.MainDbVariable);
            }

            var envConnectionStringMain = Config5Seeder.DatabaseOf(envMainConnectionString);

            if (!offenders.Contains(Config5Seeder.MainDbConnectionStringVariable) && envConnectionStringMain != null &&
                envConnectionStringMain != v5Main && IsLegacyDatabase(v5Postgres, envConnectionStringMain))
            {
                offenders.Add(Config5Seeder.MainDbConnectionStringVariable);
            }

            if (offenders.Count == 0 || !HasMigrationState(v5Postgres))
            {
                return false;
            }

            result.PendingEnvSwitch = true;
            result.SetupModeRequired = true;
            result.EnvTargets = Config5Seeder.EnvTargetsFor(offenders, v5Main, v5Log, envMainConnectionString, envLogConnectionString);

            // No install is listed, but the page still has to be signed in to. The copied Users are in the v5 database.
            result.UserSource = new LegacyDatabaseLocation
            {
                Type = DatabaseType.PostgreSQL,
                PostgresMainDb = v5Main,
                PostgresConnectionString = v5Postgres.ConnectionString
            };
            result.PendingSwitchHasUsableCredentials = HasUsableCredentials(v5Postgres, v5Config);

            return true;
        }

        private bool HasUsableCredentials(PostgresSettings v5Postgres, Dictionary<string, string> v5Config)
        {
            try
            {
                using var connection = LegacyConnections.OpenPostgres(v5Postgres.ConnectionString);

                return HasUsableCredentials(connection, DatabaseType.PostgreSQL, v5Config);
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                _logger.Debug(ex, "Unable to check the v5 database for a user to sign in with");

                return false;
            }
        }

        private static string Blank(string value) => value.IsNotNullOrWhiteSpace() ? value : null;

        private static string DatabaseOf(string connectionString) => connectionString.IsNotNullOrWhiteSpace() ? Config5Seeder.DatabaseOf(connectionString) : null;

        private bool HasMigrationState(PostgresSettings v5Postgres)
        {
            try
            {
                using var connection = LegacyConnections.OpenPostgres(v5Postgres.ConnectionString);

                return HasVersionInfo(connection, DatabaseType.PostgreSQL) &&
                       !LegacyDatabaseGuard.IsLegacy(connection, DatabaseType.PostgreSQL) &&
                       TableExists(connection, DatabaseType.PostgreSQL, "LegacyMigrationState") &&
                       Count(connection, "SELECT COUNT(*) FROM \"LegacyMigrationState\"") > 0;
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                _logger.Debug(ex, "Unable to probe the v5 database for a completed migration");

                return false;
            }
        }

        private bool IsLegacyDatabase(PostgresSettings server, string database)
        {
            try
            {
                var connectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database }.ConnectionString;

                using var connection = LegacyConnections.OpenPostgres(connectionString);

                return HasVersionInfo(connection, DatabaseType.PostgreSQL) && LegacyDatabaseGuard.IsLegacy(connection, DatabaseType.PostgreSQL);
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                _logger.Debug(ex, "Unable to probe {0}", database);

                return false;
            }
        }

        private static string DescribePostgres(PostgresSettings settings)
        {
            try
            {
                return "PostgreSQL database " + new NpgsqlConnectionStringBuilder(settings.ConnectionString).Database;
            }
            catch (Exception)
            {
                return "the configured PostgreSQL database";
            }
        }

        private static HashSet<long> LoadKnownMigrations()
        {
            return typeof(LegacyInstallDetector).Assembly.GetTypes()
                .Where(t => typeof(NzbDroneMigrationBase).IsAssignableFrom(t) && t.Namespace == "NzbDrone.Core.Datastore.Migration")
                .Select(t => t.GetCustomAttribute<FluentMigrator.MigrationAttribute>())
                .Where(a => a != null)
                .Select(a => a.Version)
                .ToHashSet();
        }

        private void TryAdd(List<LegacyInstall> installs, List<string> errors, string description, Func<LegacyInstall> detect, bool missingDatabaseIsAbsent)
        {
            try
            {
                var install = detect();

                if (install != null)
                {
                    installs.Add(install);
                }
            }
            catch (PostgresException ex) when (missingDatabaseIsAbsent && ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
            {
                // The configured database doesn't exist, so there is nothing legacy to migrate.
                _logger.Debug(ex, "{0} does not exist", description);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to inspect {0}", description);
                errors.Add($"Unable to inspect {description}: {ex.Message}");
            }
        }

        private bool HasMarkedV5Database(string appData, PostgresSettings legacyPostgres, Dictionary<string, string> v5Config)
        {
            var sqlitePath = Path.Combine(appData, V5SqliteFile);

            if (File.Exists(sqlitePath) && IsMarkedV5(() => LegacyConnections.OpenSqliteReadOnly(sqlitePath), DatabaseType.SQLite))
            {
                return true;
            }

            var v5Postgres = v5Config != null
                ? ResolvePostgres(v5Config, V5PostgresMainDb)
                : legacyPostgres.WithMainDb(V5PostgresMainDb);

            return v5Postgres.IsConfigured &&
                   IsMarkedV5(() => LegacyConnections.OpenPostgres(v5Postgres.ConnectionString), DatabaseType.PostgreSQL);
        }

        private bool IsMarkedV5(Func<IDbConnection> open, DatabaseType type)
        {
            try
            {
                using var connection = open();

                return HasVersionInfo(connection, type) && !LegacyDatabaseGuard.IsLegacy(connection, type);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
            {
                return false;
            }
            catch (NpgsqlException ex) when (ex is not PostgresException)
            {
                // The server can't be reached, so there is no v5 database to speak of. The legacy probe on the
                // same server reports the failure.
                _logger.Debug(ex, "Unable to reach the server while checking for a v5 database");

                return false;
            }
            catch (Exception ex)
            {
                // The v5 database exists but can't be probed. Normal startup surfaces its own error.
                _logger.Warn(ex, "Unable to probe the existing v5 database; treating it as marked");

                return true;
            }
        }

        private static bool HasVersionInfo(IDbConnection connection, DatabaseType type)
        {
            return type == DatabaseType.SQLite
                ? connection.ExecuteScalar<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'VersionInfo'") != null
                : connection.ExecuteScalar<string>("SELECT to_regclass('\"VersionInfo\"')::text") != null;
        }

        private static LegacyInstall DetectSqlite(string path, Dictionary<string, string> legacyConfig)
        {
            using var connection = LegacyConnections.OpenSqliteReadOnly(path);

            var location = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = path };

            return Inspect(connection, DatabaseType.SQLite, location, legacyConfig);
        }

        private static LegacyInstall DetectPostgres(PostgresSettings settings, Dictionary<string, string> legacyConfig)
        {
            using var connection = LegacyConnections.OpenPostgres(settings.ConnectionString);

            var location = new LegacyDatabaseLocation
            {
                Type = DatabaseType.PostgreSQL,
                PostgresMainDb = new NpgsqlConnectionStringBuilder(settings.ConnectionString).Database,
                PostgresConnectionString = settings.ConnectionString,
                NamedByEnvironment = settings.NamedByEnvironment
            };

            return Inspect(connection, DatabaseType.PostgreSQL, location, legacyConfig);
        }

        private static LegacyInstall Inspect(IDbConnection connection, DatabaseType type, LegacyDatabaseLocation location, Dictionary<string, string> legacyConfig)
        {
            var hasSeries = TableExists(connection, type, "Series");
            var hasMovies = TableExists(connection, type, "Movies");

            LegacyKind kind;

            if (hasMovies)
            {
                kind = LegacyKind.Eros;
            }
            else if (hasSeries)
            {
                kind = LegacyKind.V2;
            }
            else
            {
                return null;
            }

            var install = new LegacyInstall
            {
                Kind = kind,
                Database = location,
                MaxMigration = TableExists(connection, type, "VersionInfo")
                    ? connection.ExecuteScalar<long?>("SELECT MAX(\"Version\") FROM \"VersionInfo\"") ?? 0
                    : 0,
                HasUsableCredentials = HasUsableCredentials(connection, type, legacyConfig)
            };

            if (kind == LegacyKind.V2)
            {
                InspectV2(connection, install);
            }
            else
            {
                InspectEros(connection, install);
            }

            return install;
        }

        private static void InspectV2(IDbConnection connection, LegacyInstall install)
        {
            install.Studios = Count(connection, "SELECT COUNT(*) FROM \"Series\"");
            install.Scenes = Count(connection, "SELECT COUNT(*) FROM \"Episodes\"");
            install.Files = Count(connection, "SELECT COUNT(*) FROM \"EpisodeFiles\"");
            install.PreviousSceneSource = SceneMetadataType.Tpdb;
            install.PreviousMovieSource = MovieMetadataType.TPDB;

            if (install.MaxMigration < FirstSupportedV2Migration)
            {
                install.Refusal = LegacyRefusal.V2TooOld;
            }
        }

        private static void InspectEros(IDbConnection connection, LegacyInstall install)
        {
            const string moviesOfType = "SELECT COUNT(*) FROM \"Movies\" m JOIN \"MovieMetadata\" md ON md.\"Id\" = m.\"MovieMetadataId\" WHERE md.\"ItemType\" = ";

            install.Studios = Count(connection, "SELECT COUNT(*) FROM \"Studios\"");
            install.Scenes = Count(connection, moviesOfType + "1");
            install.Movies = Count(connection, moviesOfType + "0");
            install.Files = Count(connection, "SELECT COUNT(*) FROM \"MovieFiles\"");
            install.PreviousSceneSource = SceneMetadataType.StashDb;
            install.PreviousMovieSource = ReadMovieSource(connection);

            // MetadataSource: 0 Tmdb, 2 TpdbMovie.
            var movieSources = connection.Query<int>(
                "SELECT DISTINCT md.\"MetadataSource\" FROM \"Movies\" m JOIN \"MovieMetadata\" md ON md.\"Id\" = m.\"MovieMetadataId\" WHERE md.\"ItemType\" = 0 AND md.\"MetadataSource\" IN (0, 2)");

            install.HasMixedMovies = movieSources.Count() > 1;

            var versions = connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\" WHERE \"Version\" < 300");

            if (versions.Any(v => !KnownMigrations.Value.Contains(v)))
            {
                install.Refusal = LegacyRefusal.ErosNewerThanV5;
            }
        }

        private static MovieMetadataType ReadMovieSource(IDbConnection connection)
        {
            var value = connection.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE LOWER(\"Key\") = 'whisparrmoviemetadatasource'");

            // IsDefined rejects numeric strings such as "7" that TryParse accepts.
            return Enum.TryParse<MovieMetadataType>(value?.Trim(), true, out var source) && Enum.IsDefined(source)
                ? source
                : MovieMetadataType.TMDB;
        }

        private static bool HasUsableCredentials(IDbConnection connection, DatabaseType type, Dictionary<string, string> legacyConfig)
        {
            legacyConfig.TryGetValue("AuthenticationMethod", out var method);

            if (string.Equals(method, "Oidc", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(method, "Forms", StringComparison.OrdinalIgnoreCase) &&
                   TableExists(connection, type, "Users") &&
                   Count(connection, "SELECT COUNT(*) FROM \"Users\"") > 0;
        }

        private static int Count(IDbConnection connection, string sql)
        {
            return connection.ExecuteScalar<int>(sql);
        }

        private static bool TableExists(IDbConnection connection, DatabaseType type, string table)
        {
            return type == DatabaseType.SQLite
                ? connection.ExecuteScalar<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name = @table", new { table }) != null
                : connection.ExecuteScalar<string>("SELECT to_regclass(@table)::text", new { table = $"\"{table}\"" }) != null;
        }

        private Dictionary<string, string> ReadConfig(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(path))
            {
                return values;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var document = XDocument.Load(stream);

                foreach (var element in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    values[element.Name.LocalName] = element.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to read {0}", path);
            }

            return values;
        }

        // Environment values override the config file, as they do for the running app.
        // ignoreEnvironmentNames leaves out the variables that name a database, for probing where config5.xml
        // says the v5 database lives. The server variables still apply.
        private PostgresSettings ResolvePostgres(Dictionary<string, string> config, string defaultMainDb, bool ignoreEnvironmentNames = false)
        {
            string FromConfig(string key) => config.TryGetValue(key, out var value) ? value : null;

            var host = _options.Host.IsNotNullOrWhiteSpace() ? _options.Host : FromConfig("PostgresHost");
            var user = _options.User.IsNotNullOrWhiteSpace() ? _options.User : FromConfig("PostgresUser");
            var password = _options.Password.IsNotNullOrWhiteSpace() ? _options.Password : FromConfig("PostgresPassword");
            var port = 5432;

            if (_options.Port != 0)
            {
                port = _options.Port;
            }
            else if (int.TryParse(FromConfig("PostgresPort"), out var configPort))
            {
                port = configPort;
            }

            var envMainDb = !ignoreEnvironmentNames && _options.MainDb.IsNotNullOrWhiteSpace() ? _options.MainDb : null;
            var envConnectionString = !ignoreEnvironmentNames && _options.MainDbConnectionString.IsNotNullOrWhiteSpace() ? _options.MainDbConnectionString : null;
            var connectionString = envConnectionString ?? (FromConfig("PostgresMainDbConnectionString").IsNotNullOrWhiteSpace() ? FromConfig("PostgresMainDbConnectionString") : null);
            var mainDb = envMainDb ?? (FromConfig("PostgresMainDb").IsNotNullOrWhiteSpace() ? FromConfig("PostgresMainDb") : defaultMainDb);

            var settings = new PostgresSettings { NamedByEnvironment = envMainDb != null || envConnectionString != null };

            if (connectionString != null)
            {
                settings.ConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { Enlist = false }.ConnectionString;
                settings.IsConfigured = true;
            }
            else if (host.IsNotNullOrWhiteSpace())
            {
                settings.ConnectionString = new NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = port,
                    Username = user,
                    Password = password,
                    Database = mainDb,
                    Enlist = false
                }.ConnectionString;
                settings.IsConfigured = true;
            }

            return settings;
        }

        private sealed class PostgresSettings
        {
            public bool IsConfigured { get; set; }
            public string ConnectionString { get; set; }
            public bool NamedByEnvironment { get; set; }

            // Same server, different database. Never carries the environment-named flag.
            public PostgresSettings WithMainDb(string mainDb)
            {
                if (!IsConfigured)
                {
                    return this;
                }

                return new PostgresSettings
                {
                    IsConfigured = true,
                    ConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = mainDb }.ConnectionString
                };
            }
        }
    }
}
