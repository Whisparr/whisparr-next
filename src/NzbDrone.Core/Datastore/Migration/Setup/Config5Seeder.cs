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
    }

    public interface IConfig5Seeder
    {
        V5DatabaseTargets Seed(string legacyConfigPath, string config5Path, LegacyInstall install);
    }

    // Builds config5.xml from a legacy config.xml. It is built by hand in Bootstrap, before the container exists.
    public class Config5Seeder : IConfig5Seeder
    {
        public const string V5MainDb = "whisparr5-main";
        public const string V5LogDb = "whisparr5-log";

        private const string LegacyMainDb = "whisparr-main";
        private const string LegacyLogDb = "whisparr-log";
        private const string MainDbVariable = "Whisparr__Postgres__MainDb";
        private const string LogDbVariable = "Whisparr__Postgres__LogDb";
        private const string MainDbConnectionStringVariable = "Whisparr__Postgres__MainDbConnectionString";
        private const string LogDbConnectionStringVariable = "Whisparr__Postgres__LogDbConnectionString";

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

        public V5DatabaseTargets Seed(string legacyConfigPath, string config5Path, LegacyInstall install)
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

            var legacyMain = LegacyName(root, install, "PostgresMainDb", "PostgresMainDbConnectionString", LegacyMainDb, true);
            var legacyLog = LegacyName(root, install, "PostgresLogDb", "PostgresLogDbConnectionString", LegacyLogDb, false);

            var envMain = FromEnvironment(MainDbVariable);
            var envLog = FromEnvironment(LogDbVariable);

            var offenders = new List<string>();

            if (Same(envMain, legacyMain))
            {
                offenders.Add(MainDbVariable);
            }

            if (Same(envLog, legacyLog))
            {
                offenders.Add(LogDbVariable);
            }

            if (Same(DatabaseOf(FromEnvironment(MainDbConnectionStringVariable)), legacyMain))
            {
                offenders.Add(MainDbConnectionStringVariable);
            }

            if (Same(DatabaseOf(FromEnvironment(LogDbConnectionStringVariable)), legacyLog))
            {
                offenders.Add(LogDbConnectionStringVariable);
            }

            var targets = new V5DatabaseTargets
            {
                MainDb = Target(envMain, legacyMain, V5MainDb),
                LogDb = Target(envLog, legacyLog, V5LogDb),
                EnvironmentStillNamesLegacy = offenders.Count > 0,
                EnvVarsStillNamingLegacy = offenders
            };

            Set(root, "PostgresMainDb", targets.MainDb);
            Set(root, "PostgresLogDb", targets.LogDb);
            RewriteConnectionString(root, "PostgresMainDbConnectionString", targets.MainDb);
            RewriteConnectionString(root, "PostgresLogDbConnectionString", targets.LogDb);

            Write(new XDocument(new XDeclaration("1.0", "utf-8", null), root), config5Path);

            return targets;
        }

        private string FromEnvironment(string name)
        {
            var value = _getEnvironmentVariable(name);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string DatabaseOf(string connectionString)
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

        private static string LegacyName(XElement root, LegacyInstall install, string key, string connectionStringKey, string defaultName, bool main)
        {
            if (main && !string.IsNullOrWhiteSpace(install?.Database?.PostgresMainDb))
            {
                return install.Database.PostgresMainDb;
            }

            var connectionString = root.Element(connectionStringKey)?.Value;

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

            var name = root.Element(key)?.Value;

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
