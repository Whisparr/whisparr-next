using System;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Dapper;
using FluentAssertions;
using Moq;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class PreV5BackupServiceFixture : TestBase
    {
        private string _appData;
        private string _extract;
        private string _configPath;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(TempFolder, "appdata");
            _extract = Path.Combine(TempFolder, "extract");
            Directory.CreateDirectory(_appData);
            Directory.CreateDirectory(_extract);

            _configPath = Path.Combine(_appData, "config.xml");
            File.WriteAllText(_configPath, "<Config><Port>6969</Port></Config>");
        }

        private PreV5BackupService Service()
        {
            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(_appData);

            return new PreV5BackupService(folders.Object, new PostgresTableCopier());
        }

        private static LegacyInstall Sqlite(string path)
        {
            return new LegacyInstall
            {
                Kind = LegacyKind.Eros,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = path }
            };
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);

            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static long Count(string path, string table)
        {
            using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
            connection.Open();

            return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table}\"");
        }

        private string ExtractDb(string zipPath, string name)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(name);
            var target = Path.Combine(_extract, name);

            entry.ExtractToFile(target, true);

            return target;
        }

        [Test]
        public void should_write_zip_with_db_and_config()
        {
            var db = Path.Combine(_appData, "whisparr3.db");
            LegacyDbBuilder.CreateErosSqlite(db);

            var zipPath = Service().Backup(Sqlite(db), _configPath);

            zipPath.Should().StartWith(Path.Combine(_appData, "Backups", "pre-v5"));
            zipPath.Should().EndWith(".zip");

            using var zip = ZipFile.OpenRead(zipPath);

            zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo("whisparr3.db", "config.xml");

            Directory.GetDirectories(Path.Combine(_appData, "Backups", "pre-v5")).Should().BeEmpty();
        }

        [Test]
        public void should_produce_db_with_same_row_counts()
        {
            var db = Path.Combine(_appData, "whisparr3.db");
            LegacyDbBuilder.CreateErosSqlite(db);
            LegacyDbBuilder.InsertConfig(db, "a", "1");
            LegacyDbBuilder.InsertConfig(db, "b", "2");
            LegacyDbBuilder.InsertErosItem(db, "x1", 1, 3);

            var zipPath = Service().Backup(Sqlite(db), _configPath);
            var copy = ExtractDb(zipPath, "whisparr3.db");

            Count(copy, "Config").Should().Be(Count(db, "Config"));
            Count(copy, "MovieMetadata").Should().Be(1);
            Count(copy, "Movies").Should().Be(1);
        }

        [Test]
        public void should_leave_legacy_bytes_unchanged()
        {
            var db = Path.Combine(_appData, "whisparr3.db");
            LegacyDbBuilder.CreateErosSqlite(db);
            LegacyDbBuilder.EnableWal(db);

            var before = Hash(db);
            var configBefore = Hash(_configPath);

            Service().Backup(Sqlite(db), _configPath);

            Hash(db).Should().Be(before);
            Hash(_configPath).Should().Be(configBefore);
            File.Exists(db + "-wal").Should().BeFalse();
            File.Exists(db + "-shm").Should().BeFalse();
        }

        [Test]
        public void should_include_uncheckpointed_wal_rows_in_backup()
        {
            var live = Path.Combine(_appData, "live.db");
            LegacyDbBuilder.CreateErosSqlite(live);
            LegacyDbBuilder.EnableWal(live);

            var crashed = Path.Combine(_appData, "whisparr3.db");

            // A writer that never checkpoints, copied aside while open: the legacy app killed uncleanly.
            using (var writer = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = live, Pooling = false }.ConnectionString))
            {
                writer.Open();
                writer.Execute("PRAGMA wal_autocheckpoint=0");
                writer.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('wal1', 'x')");
                writer.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('wal2', 'x')");

                new FileInfo(live + "-wal").Length.Should().BeGreaterThan(0);

                foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                {
                    if (File.Exists(live + suffix))
                    {
                        File.Copy(live + suffix, crashed + suffix, true);
                    }
                }
            }

            File.Exists(crashed + "-wal").Should().BeTrue();

            var hashes = new[] { crashed, crashed + "-wal", crashed + "-shm" }
                .Where(File.Exists)
                .ToDictionary(p => p, Hash);

            var zipPath = Service().Backup(Sqlite(crashed), _configPath);
            var copy = ExtractDb(zipPath, "whisparr3.db");

            using (var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = copy, Pooling = false }.ConnectionString))
            {
                connection.Open();
                connection.Query<string>("SELECT \"Key\" FROM \"Config\" WHERE \"Key\" LIKE 'wal%'").Should().BeEquivalentTo("wal1", "wal2");
            }

            foreach (var (path, hash) in hashes)
            {
                Hash(path).Should().Be(hash);
            }
        }

        [Test]
        public void should_throw_and_leave_no_zip_when_db_missing()
        {
            var ex = Assert.Throws<SetupStepException>(() => Service().Backup(Sqlite(Path.Combine(_appData, "whisparr3.db")), _configPath));

            ex.UserMessage.Should().NotBeNullOrEmpty();

            var backups = Path.Combine(_appData, "Backups", "pre-v5");

            if (Directory.Exists(backups))
            {
                Directory.GetFileSystemEntries(backups).Should().BeEmpty();
            }
        }

        [Test]
        public void should_dump_every_table_and_schema()
        {
            var options = PostgresDatabase.GetTestOptions();

            if (string.IsNullOrEmpty(options.Host))
            {
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            PostgresDatabase.Create(options, MigrationType.Main);

            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = options.MainDb,
                Enlist = false
            }.ConnectionString;

            try
            {
                new MigrationController(LogManager.GetCurrentClassLogger())
                    .Migrate(connectionString, new MigrationContext(MigrationType.Main, LegacyDbBuilder.ErosVersion), DatabaseType.PostgreSQL);

                using (var connection = new NpgsqlConnection(connectionString))
                {
                    connection.Open();
                    connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('pg1', 'v1')");
                }

                var install = new LegacyInstall
                {
                    Kind = LegacyKind.Eros,
                    Database = new LegacyDatabaseLocation
                    {
                        Type = DatabaseType.PostgreSQL,
                        PostgresMainDb = options.MainDb,
                        PostgresConnectionString = connectionString
                    }
                };

                var zipPath = Service().Backup(install, _configPath);

                string[] tables;

                using (var connection = new NpgsqlConnection(connectionString))
                {
                    connection.Open();
                    tables = new PostgresTableCopier().ListTables(connection).ToArray();
                }

                tables.Should().Contain("Config");

                using var zip = ZipFile.OpenRead(zipPath);

                var names = zip.Entries.Select(e => e.FullName).ToList();

                names.Should().Contain("schema.sql");
                names.Should().Contain("config.xml");
                names.Should().Contain(tables.Select(t => $"data/{t}.copy"));

                using var schema = new StreamReader(zip.GetEntry("schema.sql").Open());
                var sql = schema.ReadToEnd();

                sql.Should().Contain("CREATE TABLE \"Config\"");
                sql.Should().Contain("CREATE UNIQUE INDEX");

                using var config = new StreamReader(zip.GetEntry("data/Config.copy").Open());
                config.ReadToEnd().Should().Contain("pg1\tv1");
            }
            finally
            {
                PostgresDatabase.Drop(options, MigrationType.Main);
            }
        }
    }
}
