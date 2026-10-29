using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Dapper;
using FluentAssertions;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class V5DatabaseBuilderFixture : TestBase
    {
        private readonly List<string> _createdRoles = new();
        private string _dir;
        private PostgresOptions _options;
        private string _legacyDb;
        private string _targetMain;
        private string _targetLog;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(TempFolder, "install");
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_options == null)
            {
                return;
            }

            NpgsqlConnection.ClearAllPools();

            using var admin = OpenAdmin(_options.User, _options.Password);

            foreach (var db in new[] { _legacyDb, _targetMain, _targetLog }.Where(d => d != null))
            {
                admin.Execute($"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)");
            }

            foreach (var role in _createdRoles)
            {
                admin.Execute($"DROP ROLE IF EXISTS \"{role}\"");
            }
        }

        private static V5DatabaseBuilder Builder()
        {
            return new V5DatabaseBuilder(new MigrationController(LogManager.GetCurrentClassLogger()), new PostgresTableCopier());
        }

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);

            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static long Count(string connectionString, string table)
        {
            using var connection = new SQLiteConnection(connectionString);
            connection.Open();

            return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table}\"");
        }

        private static LegacyInstall SqliteInstall(string path)
        {
            return new LegacyInstall
            {
                Kind = LegacyKind.Eros,
                MaxMigration = LegacyDbBuilder.ErosVersion,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = path }
            };
        }

        private static V5DatabaseTargets Targets()
        {
            return new V5DatabaseTargets { MainDb = "whisparr5", LogDb = "whisparr5-log" };
        }

        private string LegacyEros()
        {
            var db = Path.Combine(_dir, "whisparr3.db");

            LegacyDbBuilder.CreateErosSqlite(db);
            LegacyDbBuilder.EnableWal(db);
            LegacyDbBuilder.InsertErosItem(db, "m1", 0, 2);
            LegacyDbBuilder.InsertErosItem(db, "s1", 1, 3);
            LegacyDbBuilder.InsertErosItem(db, "s2", 1, 3);
            LegacyDbBuilder.InsertErosFile(db);

            return db;
        }

        [Test]
        public void should_copy_and_migrate_eros_db()
        {
            var db = LegacyEros();

            var built = Builder().CopyEros(SqliteInstall(db), Targets(), _ => { }, _ => { });

            built.Type.Should().Be(DatabaseType.SQLite);
            built.SqlitePartialPath.Should().Be(Path.Combine(_dir, "whisparr5.db.partial"));
            built.CreatedPostgresDatabases.Should().BeEmpty();
            built.LogConnectionString.Should().Contain("logs5.db");
            File.Exists(built.SqlitePartialPath).Should().BeTrue();

            using (var connection = new SQLiteConnection(built.MainConnectionString))
            {
                connection.Open();

                connection.ExecuteScalar<long>("SELECT MAX(\"Version\") FROM \"VersionInfo\"").Should().BeGreaterThanOrEqualTo(305);
                connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"VersionInfo\" WHERE \"Version\" = 300").Should().Be(1);
                LegacyDatabaseGuard.IsLegacy(connection, DatabaseType.SQLite).Should().BeFalse();
            }

            using var legacy = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = db, Pooling = false }.ConnectionString);
            legacy.Open();

            foreach (var table in new[] { "Movies", "MovieMetadata", "MovieFiles" })
            {
                Count(built.MainConnectionString, table).Should().Be(legacy.ExecuteScalar<long>($"SELECT COUNT(*) FROM \"{table}\""));
            }

            Count(built.MainConnectionString, "Movies").Should().Be(3);
        }

        [Test]
        public void should_leave_legacy_bytes_unchanged()
        {
            var db = LegacyEros();
            var before = Hash(db);

            Builder().CopyEros(SqliteInstall(db), Targets(), _ => { }, _ => { });

            Hash(db).Should().Be(before);
            File.Exists(db + "-wal").Should().BeFalse();
            File.Exists(db + "-shm").Should().BeFalse();
        }

        private string LegacyV2()
        {
            var db = Path.Combine(_dir, "whisparr2.db");

            LegacyDbBuilder.CreateV2SqliteFromDump(db);

            return db;
        }

        private static LegacyInstall V2SqliteInstall(string path)
        {
            return new LegacyInstall
            {
                Kind = LegacyKind.V2,
                MaxMigration = 221,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = path }
            };
        }

        [Test]
        public void create_empty_should_build_migrated_partial()
        {
            var db = LegacyV2();

            var built = Builder().CreateEmpty(V2SqliteInstall(db), Targets(), _ => { }, _ => { });

            built.Type.Should().Be(DatabaseType.SQLite);
            built.SqlitePartialPath.Should().Be(Path.Combine(_dir, "whisparr5.db.partial"));
            built.CreatedPostgresDatabases.Should().BeEmpty();
            built.LogConnectionString.Should().Contain("logs5.db");
            File.Exists(built.SqlitePartialPath).Should().BeTrue();

            using var connection = new SQLiteConnection(built.MainConnectionString);
            connection.Open();

            LegacyDatabaseGuard.IsLegacy(connection, DatabaseType.SQLite).Should().BeFalse();
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"VersionInfo\" WHERE \"Version\" = 300").Should().Be(1);
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"Movies\"").Should().Be(0);
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"LegacyV2Series\"").Should().Be(0);
        }

        [Test]
        public void create_empty_should_leave_legacy_bytes_unchanged()
        {
            var db = LegacyV2();
            var before = Hash(db);

            Builder().CreateEmpty(V2SqliteInstall(db), Targets(), _ => { }, _ => { });

            Hash(db).Should().Be(before);
            File.Exists(db + "-wal").Should().BeFalse();
            File.Exists(db + "-shm").Should().BeFalse();
        }

        [Test]
        public void create_empty_should_replace_a_stale_partial()
        {
            var db = LegacyV2();
            var partial = Path.Combine(_dir, "whisparr5.db.partial");

            File.WriteAllText(partial, "stale");
            File.WriteAllText(partial + "-wal", "stale");

            var built = Builder().CreateEmpty(V2SqliteInstall(db), Targets(), _ => { }, _ => { });

            File.Exists(partial + "-wal").Should().BeFalse();
            Count(built.MainConnectionString, "Movies").Should().Be(0);
        }

        [Test]
        public void create_empty_should_wrap_failures()
        {
            var missing = V2SqliteInstall(Path.Combine(_dir, "no-such-folder", "whisparr2.db"));

            Assert.Throws<SetupStepException>(() => Builder().CreateEmpty(missing, Targets(), _ => { }, _ => { }));
        }

        [Test]
        public void should_lock_stashdb_via_migration_301()
        {
            var db = LegacyEros();

            var built = Builder().CopyEros(SqliteInstall(db), Targets(), _ => { }, _ => { });

            using var connection = new SQLiteConnection(built.MainConnectionString);
            connection.Open();

            connection.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'metadatasourceslocked'").Should().Be("true");
        }

        [Test]
        public void should_include_uncheckpointed_wal_rows_in_copy()
        {
            var live = Path.Combine(_dir, "live.db");
            LegacyDbBuilder.CreateErosSqlite(live);
            LegacyDbBuilder.EnableWal(live);

            var crashed = Path.Combine(_dir, "whisparr3.db");

            using (var writer = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = live, Pooling = false }.ConnectionString))
            {
                writer.Open();
                writer.Execute("PRAGMA wal_autocheckpoint=0");
                writer.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('wal1', 'x')");

                new FileInfo(live + "-wal").Length.Should().BeGreaterThan(0);

                foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                {
                    if (File.Exists(live + suffix))
                    {
                        File.Copy(live + suffix, crashed + suffix, true);
                    }
                }
            }

            var hashes = new[] { crashed, crashed + "-wal", crashed + "-shm" }.Where(File.Exists).ToDictionary(p => p, Hash);

            var built = Builder().CopyEros(SqliteInstall(crashed), Targets(), _ => { }, _ => { });

            using (var connection = new SQLiteConnection(built.MainConnectionString))
            {
                connection.Open();
                connection.Query<string>("SELECT \"Key\" FROM \"Config\" WHERE \"Key\" = 'wal1'").Should().ContainSingle();
            }

            foreach (var (path, hash) in hashes)
            {
                Hash(path).Should().Be(hash);
            }
        }

        [Test]
        public void should_replace_a_stale_partial_and_remove_it_on_failure()
        {
            var db = LegacyEros();
            var partial = Path.Combine(_dir, "whisparr5.db.partial");

            File.WriteAllText(partial, "stale");
            File.WriteAllText(partial + "-wal", "stale");

            Builder().CopyEros(SqliteInstall(db), Targets(), _ => { }, _ => { });

            File.Exists(partial + "-wal").Should().BeFalse();

            File.Delete(db);
            File.WriteAllText(db, "not a database");

            Assert.Throws<SetupStepException>(() => Builder().CopyEros(SqliteInstall(db), Targets(), _ => { }, _ => { }));

            File.Exists(partial).Should().BeFalse();
        }

        [Test]
        public void snapshot_should_ignore_stale_files_in_temp_dir()
        {
            var db = Path.Combine(_dir, "whisparr3.db");
            LegacyDbBuilder.CreateErosSqlite(db);
            LegacyDbBuilder.EnableWal(db);

            // A -wal next to the legacy file forces the copy path.
            File.WriteAllBytes(db + "-wal", Array.Empty<byte>());

            var temp = Path.Combine(_dir, "snap");
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "whisparr3.db-wal"), "garbage from an earlier run");

            using (var snapshot = LegacySqliteSnapshot.Open(db, temp))
            {
                snapshot.Connection.Query<long>("SELECT COUNT(*) FROM \"VersionInfo\"").Single().Should().BeGreaterThan(0);
            }

            File.ReadAllText(Path.Combine(temp, "whisparr3.db-wal")).Should().Be("garbage from an earlier run");
            Directory.GetDirectories(temp).Should().BeEmpty();
        }

        [Test]
        public void copier_should_round_trip_a_table()
        {
            StartPostgres();

            using var connection = new NpgsqlConnection(PgConnectionString(_legacyDb));
            connection.Open();

            connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('k1', 'tab\there'), ('k2', 'line\nbreak')");
            connection.Execute("CREATE TABLE \"ConfigCopy\" (LIKE \"Config\" INCLUDING DEFAULTS)");

            var copier = new PostgresTableCopier();

            using var stream = new MemoryStream();
            copier.CopyOut(connection, "Config", stream);
            stream.Position = 0;
            copier.CopyIn(connection, "ConfigCopy", stream);

            var original = connection.Query<(int Id, string Key, string Value)>("SELECT \"Id\", \"Key\", \"Value\" FROM \"Config\" ORDER BY \"Id\"").ToList();
            var copy = connection.Query<(int Id, string Key, string Value)>("SELECT \"Id\", \"Key\", \"Value\" FROM \"ConfigCopy\" ORDER BY \"Id\"").ToList();

            copy.Should().Equal(original);
            copy.Should().Contain(r => r.Value == "tab\there");
            copy.Should().Contain(r => r.Value == "line\nbreak");
        }

        // PostgreSQL

        private void StartPostgres()
        {
            _options = PostgresDatabase.GetTestOptions();

            if (string.IsNullOrEmpty(_options.Host))
            {
                _options = null;
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            _legacyDb = _options.MainDb;
            _targetMain = _options.MainDb + "_v5";
            _targetLog = _options.LogDb + "_v5";

            PostgresDatabase.Create(_options, MigrationType.Main);

            new MigrationController(LogManager.GetCurrentClassLogger())
                .Migrate(PgConnectionString(_legacyDb), new MigrationContext(MigrationType.Main, LegacyDbBuilder.ErosVersion), DatabaseType.PostgreSQL);

            using var connection = new NpgsqlConnection(PgConnectionString(_legacyDb));
            connection.Open();
            connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('pg1', 'v1'), ('pg2', 'v2')");
            connection.Execute("INSERT INTO \"Tags\" (\"Label\") VALUES ('a'), ('b')");
        }

        private string PgConnectionString(string database, string user = null, string password = null)
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = _options.Host,
                Port = _options.Port,
                Username = user ?? _options.User,
                Password = password ?? _options.Password,
                Database = database,
                Enlist = false,
                Pooling = false
            }.ConnectionString;
        }

        private NpgsqlConnection OpenAdmin(string user, string password)
        {
            var connection = new NpgsqlConnection(PgConnectionString("postgres", user, password));
            connection.Open();

            return connection;
        }

        private LegacyInstall PgInstall(string user = null, string password = null)
        {
            return new LegacyInstall
            {
                Kind = LegacyKind.Eros,
                MaxMigration = LegacyDbBuilder.ErosVersion,
                Database = new LegacyDatabaseLocation
                {
                    Type = DatabaseType.PostgreSQL,
                    PostgresMainDb = _legacyDb,
                    PostgresConnectionString = PgConnectionString(_legacyDb, user, password)
                }
            };
        }

        private V5DatabaseTargets PgTargets()
        {
            return new V5DatabaseTargets { MainDb = _targetMain, LogDb = _targetLog };
        }

        private bool Exists(string db)
        {
            using var admin = OpenAdmin(_options.User, _options.Password);

            return admin.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @db)", new { db });
        }

        private void AssertCopied(BuiltDatabases built)
        {
            using var connection = new NpgsqlConnection(built.MainConnectionString);
            connection.Open();

            LegacyDatabaseGuard.IsLegacy(connection, DatabaseType.PostgreSQL).Should().BeFalse();
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"Config\" WHERE \"Key\" IN ('pg1', 'pg2')").Should().Be(2);
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"Tags\"").Should().Be(2);

            // The id sequences continue after the copied rows.
            connection.Execute("INSERT INTO \"Tags\" (\"Label\") VALUES ('c')");
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"Tags\"").Should().Be(3);

            Exists(_targetLog).Should().BeTrue();
        }

        [Test]
        public void should_template_copy()
        {
            StartPostgres();

            var created = new List<string>();

            var built = Builder().CopyEros(PgInstall(), PgTargets(), created.Add, _ => { });

            built.Type.Should().Be(DatabaseType.PostgreSQL);
            built.SqlitePartialPath.Should().BeNull();
            built.CreatedPostgresDatabases.Should().Equal(_targetMain, _targetLog);
            created.Should().Equal(_targetMain, _targetLog);

            AssertCopied(built);
        }

        [Test]
        public void create_empty_should_create_both_databases_without_template()
        {
            StartPostgres();

            var created = new List<string>();

            var built = Builder().CreateEmpty(PgInstall(), PgTargets(), created.Add, _ => { });

            built.Type.Should().Be(DatabaseType.PostgreSQL);
            built.SqlitePartialPath.Should().BeNull();
            built.CreatedPostgresDatabases.Should().Equal(_targetMain, _targetLog);
            created.Should().Equal(_targetMain, _targetLog);

            using var connection = new NpgsqlConnection(built.MainConnectionString);
            connection.Open();

            LegacyDatabaseGuard.IsLegacy(connection, DatabaseType.PostgreSQL).Should().BeFalse();

            // Nothing of the legacy rows came along.
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"Config\" WHERE \"Key\" IN ('pg1', 'pg2')").Should().Be(0);
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"Tags\"").Should().Be(0);
            Exists(_targetLog).Should().BeTrue();
        }

        [Test]
        public void create_empty_should_reuse_an_empty_target_and_refuse_a_non_empty_one()
        {
            StartPostgres();

            using (var admin = OpenAdmin(_options.User, _options.Password))
            {
                admin.Execute($"CREATE DATABASE \"{_targetMain}\"");
            }

            var created = new List<string>();
            var reused = new List<string>();

            Builder().CreateEmpty(PgInstall(), PgTargets(), created.Add, reused.Add);

            created.Should().Equal(_targetLog);
            reused.Should().Equal(_targetMain);

            // Now populated, so a second run is refused.
            var ex = Assert.Throws<SetupStepException>(() => Builder().CreateEmpty(PgInstall(), PgTargets(), _ => { }, _ => { }));

            ex.UserMessage.Should().Contain(_targetMain);
        }

        [Test]
        public void create_empty_should_report_sql_when_create_denied()
        {
            StartPostgres();

            var role = "nocreate_" + TestBase.GetUID();
            _createdRoles.Add(role);

            using (var admin = OpenAdmin(_options.User, _options.Password))
            {
                admin.Execute($"CREATE ROLE \"{role}\" LOGIN PASSWORD 'pw' NOCREATEDB");
            }

            var ex = Assert.Throws<SetupStepException>(() => Builder().CreateEmpty(PgInstall(role, "pw"), PgTargets(), _ => { }, _ => { }));

            ex.UserMessage.Should().Contain($"CREATE DATABASE \"{_targetMain}\"");
            ex.UserMessage.Should().Contain($"CREATE DATABASE \"{_targetLog}\"");
        }

        [Test]
        public void should_fall_back_to_streaming_when_template_fails()
        {
            StartPostgres();

            // An open session on the legacy database makes CREATE DATABASE ... TEMPLATE fail.
            using var blocker = new NpgsqlConnection(PgConnectionString(_legacyDb));
            blocker.Open();

            var built = Builder().CopyEros(PgInstall(), PgTargets(), _ => { }, _ => { });

            built.CreatedPostgresDatabases.Should().Equal(_targetMain, _targetLog);

            AssertCopied(built);
        }

        [Test]
        public void should_refuse_target_equal_to_legacy_name()
        {
            StartPostgres();

            var targets = new V5DatabaseTargets { MainDb = _legacyDb, LogDb = _targetLog };

            var ex = Assert.Throws<SetupStepException>(() => Builder().CopyEros(PgInstall(), targets, _ => { }, _ => { }));

            ex.UserMessage.Should().Contain(_legacyDb);
            Exists(_targetLog).Should().BeFalse();

            targets = new V5DatabaseTargets { MainDb = _targetMain, LogDb = Config5Seeder.LegacyLogDb };

            Assert.Throws<SetupStepException>(() => Builder().CopyEros(PgInstall(), targets, _ => { }, _ => { }));
            Exists(_targetMain).Should().BeFalse();
        }

        [Test]
        public void should_refuse_non_empty_existing_target()
        {
            StartPostgres();

            using (var admin = OpenAdmin(_options.User, _options.Password))
            {
                admin.Execute($"CREATE DATABASE \"{_targetMain}\"");
            }

            using (var connection = new NpgsqlConnection(PgConnectionString(_targetMain)))
            {
                connection.Open();
                connection.Execute("CREATE TABLE \"Something\" (\"Id\" integer)");
            }

            var ex = Assert.Throws<SetupStepException>(() => Builder().CopyEros(PgInstall(), PgTargets(), _ => { }, _ => { }));

            ex.UserMessage.Should().Contain(_targetMain);
            Exists(_targetLog).Should().BeFalse();
        }

        [Test]
        public void should_stream_into_an_existing_empty_target()
        {
            StartPostgres();

            using (var admin = OpenAdmin(_options.User, _options.Password))
            {
                admin.Execute($"CREATE DATABASE \"{_targetMain}\"");
            }

            var created = new List<string>();
            var reused = new List<string>();

            var built = Builder().CopyEros(PgInstall(), PgTargets(), created.Add, reused.Add);

            created.Should().Equal(_targetLog);
            reused.Should().Equal(_targetMain);

            AssertCopied(built);
        }

        [Test]
        public void should_not_report_a_reused_target_when_a_target_is_refused()
        {
            StartPostgres();

            using (var admin = OpenAdmin(_options.User, _options.Password))
            {
                admin.Execute($"CREATE DATABASE \"{_targetMain}\"");
                admin.Execute($"CREATE DATABASE \"{_targetLog}\"");
            }

            using (var connection = new NpgsqlConnection(PgConnectionString(_targetLog)))
            {
                connection.Open();
                connection.Execute("CREATE TABLE \"Something\" (\"Id\" integer)");
            }

            var reused = new List<string>();

            Assert.Throws<SetupStepException>(() => Builder().CopyEros(PgInstall(), PgTargets(), _ => { }, reused.Add));

            reused.Should().BeEmpty();
        }

        [Test]
        public void should_refuse_a_custom_legacy_log_name()
        {
            StartPostgres();

            var targets = new V5DatabaseTargets { MainDb = _targetMain, LogDb = _targetLog, LegacyLogDb = _targetLog };

            var ex = Assert.Throws<SetupStepException>(() => Builder().CopyEros(PgInstall(), targets, _ => { }, _ => { }));

            ex.UserMessage.Should().Contain(_targetLog);
            Exists(_targetMain).Should().BeFalse();
        }

        [Test]
        public void should_report_sql_without_env_vars_when_create_denied()
        {
            StartPostgres();

            var role = "nocreate_" + TestBase.GetUID();
            _createdRoles.Add(role);

            using (var admin = OpenAdmin(_options.User, _options.Password))
            {
                admin.Execute($"CREATE ROLE \"{role}\" LOGIN PASSWORD 'pw' NOCREATEDB");
            }

            var created = new List<string>();

            var ex = Assert.Throws<SetupStepException>(() => Builder().CopyEros(PgInstall(role, "pw"), PgTargets(), created.Add, _ => { }));

            created.Should().BeEmpty();
            ex.UserMessage.Should().Contain($"CREATE DATABASE \"{_targetMain}\"");
            ex.UserMessage.Should().Contain($"CREATE DATABASE \"{_targetLog}\"");

            // Pointing the env vars at the empty databases would hide the legacy install on the next start.
            ex.UserMessage.Should().NotContain("Whisparr__Postgres__MainDb");
            ex.UserMessage.Should().NotContain("Whisparr__Postgres__LogDb");
        }
    }
}
