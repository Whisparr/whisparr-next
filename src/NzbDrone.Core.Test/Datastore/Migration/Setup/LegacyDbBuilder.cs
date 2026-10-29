using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    // Builds throwaway legacy databases. Only ever writes files it creates itself.
    public static class LegacyDbBuilder
    {
        // The last Eros migration, so a fresh database migrated this far has the Eros schema.
        public const long ErosVersion = 247;

        public static void CreateErosSqlite(string path)
        {
            var connectionString = new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString;

            new MigrationController(LogManager.GetCurrentClassLogger())
                .Migrate(connectionString, new MigrationContext(MigrationType.Main, ErosVersion), DatabaseType.SQLite);
        }

        public static void CreateV5Sqlite(string path)
        {
            var connectionString = new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString;

            new MigrationController(LogManager.GetCurrentClassLogger())
                .Migrate(connectionString, new MigrationContext(MigrationType.Main), DatabaseType.SQLite);
        }

        // Persistent WAL mode, as real installs use. Closing the only connection checkpoints and removes -wal/-shm.
        public static void EnableWal(string path)
        {
            Execute(path, "PRAGMA journal_mode=WAL");
        }

        public static void DropTable(string path, string table)
        {
            Execute(path, $"DROP TABLE \"{table}\"");
        }

        public static void CreateV2Sqlite(string path, long maxVersion)
        {
            using var connection = OpenSqlite(path);

            connection.Execute("CREATE TABLE \"VersionInfo\" (\"Version\" INTEGER NOT NULL, \"AppliedOn\" DATETIME, \"Description\" TEXT)");

            for (var version = 1L; version <= maxVersion; version++)
            {
                connection.Execute("INSERT INTO \"VersionInfo\" (\"Version\", \"Description\") VALUES (@version, 'v2')", new { version });
            }

            connection.Execute("CREATE TABLE \"Series\" (\"Id\" INTEGER PRIMARY KEY, \"TvdbId\" INTEGER, \"Title\" TEXT, \"Monitored\" INTEGER, \"Path\" TEXT)");
            connection.Execute("CREATE TABLE \"Episodes\" (\"Id\" INTEGER PRIMARY KEY, \"SeriesId\" INTEGER, \"Title\" TEXT, \"Monitored\" INTEGER, \"EpisodeFileId\" INTEGER)");
            connection.Execute("CREATE TABLE \"EpisodeFiles\" (\"Id\" INTEGER PRIMARY KEY, \"SeriesId\" INTEGER, \"RelativePath\" TEXT)");
            connection.Execute("CREATE TABLE \"Users\" (\"Id\" INTEGER PRIMARY KEY, \"Identifier\" TEXT, \"Username\" TEXT, \"Password\" TEXT)");
        }

        // A real v2 database at schema 221, from a fresh run of v2 develop (c55fdc66a).
        public static void CreateV2SqliteFromDump(string path)
        {
            using var connection = OpenSqlite(path);

            connection.Execute(File.ReadAllText(DumpPath("v2-fresh-221.sql")));
        }

        // The same database on PostgreSQL 16 (pg_dump --inserts). The database must exist and be empty.
        public static void CreateV2PostgresFromDump(string connectionString)
        {
            var lines = File.ReadAllLines(DumpPath("v2-fresh-221.pg.sql")).Where(l => !l.StartsWith('\\'));
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };

            using (var connection = new NpgsqlConnection(builder.ConnectionString))
            {
                connection.Open();
                connection.Execute(string.Join("\n", lines));
            }
        }

        // Works on either database. Identifiers are quoted, values go in as parameters.
        public static void InsertV2Row(IDbConnection connection, string table, object values)
        {
            var properties = values.GetType().GetProperties();
            var columns = string.Join(", ", properties.Select(p => $"\"{p.Name}\""));
            var parameters = string.Join(", ", properties.Select(p => "@" + p.Name));

            connection.Execute($"INSERT INTO \"{table}\" ({columns}) VALUES ({parameters})", values);
        }

        private static string DumpPath(string name)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "LegacyV2", name);
        }

        public static void InsertSeries(string path, int id)
        {
            Execute(path, "INSERT INTO \"Series\" (\"Id\", \"TvdbId\", \"Title\", \"Monitored\", \"Path\") VALUES (@id, @id, 'Studio', 1, '/media/studio')", new { id });
        }

        public static void InsertEpisode(string path, int id, int seriesId, int fileId = 0)
        {
            Execute(path, "INSERT INTO \"Episodes\" (\"Id\", \"SeriesId\", \"Title\", \"Monitored\", \"EpisodeFileId\") VALUES (@id, @seriesId, 'Scene', 1, @fileId)", new { id, seriesId, fileId });
        }

        public static void InsertEpisodeFile(string path, int id, int seriesId)
        {
            Execute(path, "INSERT INTO \"EpisodeFiles\" (\"Id\", \"SeriesId\", \"RelativePath\") VALUES (@id, @seriesId, 'scene.mp4')", new { id, seriesId });
        }

        public static void InsertStudio(string path, string foreignId)
        {
            // Later migrations add NOT NULL columns without defaults, so fill whatever the schema requires.
            using var connection = OpenSqlite(path);

            var values = new Dictionary<string, object>
            {
                ["ForeignId"] = foreignId,
                ["Title"] = "Studio",
                ["CleanTitle"] = "studio",
                ["RootFolderPath"] = "/media",
                ["Tags"] = "[]"
            };

            var columns = connection.Query("PRAGMA table_info(\"Studios\")");

            foreach (var column in columns)
            {
                var name = (string)column.name;
                var required = (long)column.notnull == 1 && column.dflt_value == null && (long)column.pk == 0;

                if (required && !values.ContainsKey(name))
                {
                    values[name] = ((string)column.type).Contains("INT", System.StringComparison.OrdinalIgnoreCase) ? 0 : string.Empty;
                }
            }

            var names = string.Join(", ", values.Keys.Select(k => $"\"{k}\""));
            var parameters = string.Join(", ", values.Keys.Select(k => "@" + k));

            connection.Execute($"INSERT INTO \"Studios\" ({names}) VALUES ({parameters})", values);
        }

        // itemType: 0 movie, 1 scene. metadataSource: 0 Tmdb, 1 Stash, 2 TpdbMovie, 3 TpdbScene.
        public static void InsertErosItem(string path, string foreignId, int itemType, int metadataSource)
        {
            Execute(
                path,
                "INSERT INTO \"MovieMetadata\" (\"ForeignId\", \"MetadataSource\", \"Images\", \"Title\", \"OriginalLanguage\", \"Status\", \"Runtime\", \"Recommendations\", \"ItemType\") " +
                "VALUES (@foreignId, @metadataSource, '[]', 'Title', 1, 0, 0, '[]', @itemType)",
                new { foreignId, metadataSource, itemType });

            Execute(
                path,
                "INSERT INTO \"Movies\" (\"Path\", \"Monitored\", \"QualityProfileId\", \"MovieMetadataId\") VALUES ('/media/x', 1, 1, (SELECT \"Id\" FROM \"MovieMetadata\" WHERE \"ForeignId\" = @foreignId))",
                new { foreignId });
        }

        public static void InsertErosFile(string path)
        {
            Execute(path, "INSERT INTO \"MovieFiles\" (\"MovieId\", \"Quality\", \"Size\", \"DateAdded\", \"RelativePath\") VALUES (1, '{}', 1, '2024-01-01', 'x.mp4')");
        }

        public static void InsertConfig(string path, string key, string value)
        {
            Execute(path, "INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES (@key, @value)", new { key, value });
        }

        public static void InsertUser(string path)
        {
            Execute(path, "INSERT INTO \"Users\" (\"Identifier\", \"Username\", \"Password\") VALUES ('id', 'user', 'pw')");
        }

        public static void InsertVersion(string path, long version)
        {
            Execute(path, "INSERT INTO \"VersionInfo\" (\"Version\", \"Description\") VALUES (@version, 'future')", new { version });
        }

        private static void Execute(string path, string sql, object parameters = null)
        {
            using var connection = OpenSqlite(path);

            connection.Execute(sql, parameters);
        }

        private static IDbConnection OpenSqlite(string path)
        {
            var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
            connection.Open();

            return connection;
        }
    }
}
