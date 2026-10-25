using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Npgsql;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public class V5DatabaseTargets
    {
        public string MainDb { get; set; }
        public string LogDb { get; set; }

        // True when an environment variable still names the legacy main or log database, so the user has to
        // change it before v5 can use its own databases.
        public bool EnvironmentStillNamesLegacy { get; set; }

        // The exact environment variables that still name a legacy database. Never null.
        public List<string> EnvVarsStillNamingLegacy { get; set; } = new();

        // The value each variable in EnvVarsStillNamingLegacy needs.
        public List<EnvTarget> EnvTargets { get; set; } = new();
    }

    public interface IConfig5Seeder
    {
        V5DatabaseTargets Seed(string legacyConfigPath, string config5Path, LegacyInstall install);

        // The targets Seed would pick, without writing anything. Reads the legacy config.xml only.
        V5DatabaseTargets PlanTargets(string legacyConfigPath, LegacyInstall install);
    }

    // Builds config5.xml from a legacy config.xml. It is built by hand in Bootstrap, before the container exists.
    public class Config5Seeder : IConfig5Seeder
    {
        public const string V5MainDb = "whisparr5-main";
        public const string V5LogDb = "whisparr5-log";

        private const string LegacyMainDb = "whisparr-main";
        public const string LegacyLogDb = "whisparr-log";
        public const string UnchangedMarker = "<unchanged>";
        public const string MainDbVariable = "Whisparr__Postgres__MainDb";
        public const string LogDbVariable = "Whisparr__Postgres__LogDb";
        public const string MainDbConnectionStringVariable = "Whisparr__Postgres__MainDbConnectionString";
        public const string LogDbConnectionStringVariable = "Whisparr__Postgres__LogDbConnectionString";

        // What each environment variable has to say for v5 to use its own databases: the database name, or for a
        // connection string the user's own string with only Database= replaced. Null for a variable that is not set.
        public static List<EnvTarget> EnvTargetsFor(IEnumerable<string> variables, string v5Main, string v5Log, string envMainConnectionString, string envLogConnectionString)
        {
            string Replace(string connectionString, string database)
            {
                try
                {
                    var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };

                    // Display only, and it reaches the API, so never carry the password.
                    if (!string.IsNullOrEmpty(builder.Password))
                    {
                        builder.Password = UnchangedMarker;
                    }

                    return builder.ConnectionString;
                }
                catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException)
                {
                    return database;
                }
            }

            return variables.Select(name => new EnvTarget
            {
                Name = name,
                Value = name switch
                {
                    MainDbVariable => v5Main,
                    LogDbVariable => v5Log,
                    MainDbConnectionStringVariable => Replace(envMainConnectionString, v5Main),
                    LogDbConnectionStringVariable => Replace(envLogConnectionString, v5Log),
                    _ => null
                }
            }).ToList();
        }

        // Source: keys read by v2's ConfigFileProvider (~/Git/Whisparrv2) against v5's. The diff found no key
        // that moved to a new name. v2-only: SslKeyPath (v5 has no equivalent, copied unchanged). v5-only:
        // ConsoleLogFormat, LogDbEnabled (new in v5, left to their defaults).
        private static readonly Dictionary<string, string> V2KeyRenames = new(StringComparer.OrdinalIgnoreCase);

        private readonly Func<string, string> _getEnvironmentVariable;

        public Config5Seeder()
            : this(Environment.GetEnvironmentVariable)
        {
        }

        public Config5Seeder(Func<string, string> getEnvironmentVariable)
        {
            _getEnvironmentVariable = getEnvironmentVariable;
        }

        public V5DatabaseTargets PlanTargets(string legacyConfigPath, LegacyInstall install)
        {
            var root = BuildRoot(legacyConfigPath);

            return Plan(root, install);
        }

        public V5DatabaseTargets Seed(string legacyConfigPath, string config5Path, LegacyInstall install)
        {
            var root = BuildRoot(legacyConfigPath);
            var targets = Plan(root, install);

            Set(root, "PostgresMainDb", targets.MainDb);
            Set(root, "PostgresLogDb", targets.LogDb);
            RewriteConnectionString(root, "PostgresMainDbConnectionString", targets.MainDb);
            RewriteConnectionString(root, "PostgresLogDbConnectionString", targets.LogDb);

            Write(new XDocument(new XDeclaration("1.0", "utf-8", null), root), config5Path);

            return targets;
        }

        private static XElement BuildRoot(string legacyConfigPath)
        {
            XDocument legacy;

            try
            {
                using var stream = new FileStream(legacyConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                legacy = XDocument.Load(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                throw new InvalidConfigFileException($"{legacyConfigPath} could not be read: {ex.Message}", ex);
            }

            var root = new XElement("Config");

            foreach (var element in legacy.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var name = V2KeyRenames.TryGetValue(element.Name.LocalName, out var renamed) ? renamed : element.Name.LocalName;

                root.Add(new XElement(name, element.Value));
            }

            return root;
        }

        private V5DatabaseTargets Plan(XElement root, LegacyInstall install)
        {
            string FromRoot(string key) => root.Element(key)?.Value;

            var legacyMain = LegacyMainName(FromRoot, install?.Database?.PostgresMainDb);
            var legacyLog = LegacyLogName(FromRoot);

            var envMain = FromEnvironment(MainDbVariable);
            var envLog = FromEnvironment(LogDbVariable);

            var envMainConnectionString = FromEnvironment(MainDbConnectionStringVariable);
            var envLogConnectionString = FromEnvironment(LogDbConnectionStringVariable);

            var offenders = EnvVarsNamingLegacy(
                legacyMain,
                legacyLog,
                envMain,
                envLog,
                envMainConnectionString,
                envLogConnectionString);

            var mainDb = Target(envMain, legacyMain, V5MainDb);
            var logDb = Target(envLog, legacyLog, V5LogDb);

            return new V5DatabaseTargets
            {
                MainDb = mainDb,
                LogDb = logDb,
                EnvironmentStillNamesLegacy = offenders.Count > 0,
                EnvVarsStillNamingLegacy = offenders,
                EnvTargets = EnvTargetsFor(offenders, mainDb, logDb, envMainConnectionString, envLogConnectionString)
            };
        }

        private string FromEnvironment(string name)
        {
            var value = _getEnvironmentVariable(name);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        // The environment variables, out of the four that can name a database, that name a legacy one. The
        // detector shares this with the seeder, so both agree on what "still names legacy" means.
        public static List<string> EnvVarsNamingLegacy(string legacyMain, string legacyLog, string envMain, string envLog, string envMainConnectionString, string envLogConnectionString)
        {
            var offenders = new List<string>();

            if (Same(envMain, legacyMain))
            {
                offenders.Add(MainDbVariable);
            }

            if (Same(envLog, legacyLog))
            {
                offenders.Add(LogDbVariable);
            }

            if (Same(DatabaseOf(envMainConnectionString), legacyMain))
            {
                offenders.Add(MainDbConnectionStringVariable);
            }

            if (Same(DatabaseOf(envLogConnectionString), legacyLog))
            {
                offenders.Add(LogDbConnectionStringVariable);
            }

            return offenders;
        }

        // The legacy main database name from the config values, as the legacy app would resolve it without the environment.
        public static string LegacyMainName(Func<string, string> configValue, string installMainDb)
        {
            return LegacyName(configValue, installMainDb, "PostgresMainDb", "PostgresMainDbConnectionString", LegacyMainDb);
        }

        public static string LegacyLogName(Func<string, string> configValue)
        {
            return LegacyName(configValue, null, "PostgresLogDb", "PostgresLogDbConnectionString", LegacyLogDb);
        }

        public static string DatabaseOf(string connectionString)
        {
            if (connectionString == null)
            {
                return null;
            }

            try
            {
                var database = new NpgsqlConnectionStringBuilder(connectionString).Database;

                return string.IsNullOrWhiteSpace(database) ? null : database;
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException)
            {
                return null;
            }
        }

        private static bool Same(string a, string b)
        {
            return a != null && string.Equals(a, b, StringComparison.Ordinal);
        }

        private static string Target(string envName, string legacyName, string v5Default)
        {
            return envName != null && !Same(envName, legacyName) ? envName : v5Default;
        }

        private static string LegacyName(Func<string, string> configValue, string installName, string key, string connectionStringKey, string defaultName)
        {
            if (!string.IsNullOrWhiteSpace(installName))
            {
                return installName;
            }

            var connectionString = configValue(connectionStringKey);

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                try
                {
                    var database = new NpgsqlConnectionStringBuilder(connectionString).Database;

                    if (!string.IsNullOrWhiteSpace(database))
                    {
                        return database;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException)
                {
                    // Unparseable, fall through to the name keys.
                }
            }

            var name = configValue(key);

            return string.IsNullOrWhiteSpace(name) ? defaultName : name;
        }

        private static void Set(XElement root, string key, string value)
        {
            var element = root.Element(key);

            if (element == null)
            {
                root.Add(new XElement(key, value));
            }
            else
            {
                element.Value = value;
            }
        }

        private static void RewriteConnectionString(XElement root, string key, string database)
        {
            var element = root.Element(key);

            if (element == null || string.IsNullOrWhiteSpace(element.Value))
            {
                return;
            }

            try
            {
                element.Value = new NpgsqlConnectionStringBuilder(element.Value) { Database = database }.ConnectionString;
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException)
            {
                // Leave an unparseable string alone; the user's value is still what they wrote.
            }
        }

        private static void Write(XDocument document, string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            var temp = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                document.Save(temp);
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
    }
}
