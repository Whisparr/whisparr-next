using System;
using System.Data.SQLite;
using System.IO;
using Dapper;
using FluentAssertions;
using Moq;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class SetupRunStateFixture : TestBase
    {
        private string _appData;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);
        }

        private string Path5(string name) => Path.Combine(_appData, name);

        private SetupRunState GivenState(PostgresOptions options = null)
        {
            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(_appData);

            return new SetupRunState(folders.Object) { PostgresOptions = options ?? new PostgresOptions() };
        }

        [Test]
        public void discard_should_drop_only_recorded_items()
        {
            var recorded = Path5("whisparr5.db.partial");
            var unrecorded = Path5("whisparr5.db.partial.other");
            File.WriteAllText(recorded, "x");
            File.WriteAllText(unrecorded, "x");

            var state = GivenState();
            state.Begin(false);
            state.RecordSqlitePartial(recorded);

            GivenState().DiscardIfPresent().Should().BeTrue();

            File.Exists(recorded).Should().BeFalse();
            File.Exists(unrecorded).Should().BeTrue();
            File.Exists(Path5("v5-setup.json")).Should().BeFalse();
        }

        [Test]
        public void discard_should_delete_config5_seeded_by_wizard()
        {
            File.WriteAllText(Path5("config5.xml"), "<Config />");

            GivenState().Begin(true);

            GivenState().DiscardIfPresent().Should().BeTrue();

            File.Exists(Path5("config5.xml")).Should().BeFalse();
        }

        [Test]
        public void discard_should_keep_config5_not_seeded_by_wizard()
        {
            File.WriteAllText(Path5("config5.xml"), "<Config />");

            GivenState().Begin(false);

            GivenState().DiscardIfPresent();

            File.Exists(Path5("config5.xml")).Should().BeTrue();
            File.Exists(Path5("v5-setup.json")).Should().BeFalse();
        }

        [Test]
        public void discard_should_return_false_without_sidecar()
        {
            File.WriteAllText(Path5("config5.xml"), "<Config />");

            GivenState().DiscardIfPresent().Should().BeFalse();

            File.Exists(Path5("config5.xml")).Should().BeTrue();
        }

        [Test]
        public void complete_should_delete_sidecar()
        {
            var state = GivenState();
            state.Begin(true);
            File.Exists(Path5("v5-setup.json")).Should().BeTrue();

            state.Complete();

            File.Exists(Path5("v5-setup.json")).Should().BeFalse();
            GivenState().DiscardIfPresent().Should().BeFalse();
        }

        [Test]
        public void discard_should_keep_sidecar_with_the_db_when_a_drop_fails()
        {
            // Nothing listens on port 1, so the drop cannot connect.
            var unreachable = new PostgresOptions { Host = "127.0.0.1", Port = 1, User = "u", Password = "p" };
            var partial = Path5("whisparr5.db.partial");
            File.WriteAllText(partial, "x");
            File.WriteAllText(Path5("config5.xml"), "<Config />");

            var state = GivenState(unreachable);
            state.Begin(true);
            state.RecordDatabase("stuck_db");
            state.RecordSqlitePartial(partial);

            GivenState(unreachable).DiscardIfPresent().Should().BeTrue();

            // What could be removed is gone, the database that could not be dropped stays recorded.
            File.Exists(partial).Should().BeFalse();
            File.Exists(Path5("config5.xml")).Should().BeFalse();
            File.Exists(Path5("v5-setup.json")).Should().BeTrue();
            File.ReadAllText(Path5("v5-setup.json")).Should().Contain("stuck_db");

            // The next start retries it, and keeps it while the server stays unreachable.
            GivenState(unreachable).DiscardIfPresent().Should().BeTrue();
            File.ReadAllText(Path5("v5-setup.json")).Should().Contain("stuck_db");
        }

        [Test]
        public void discard_should_empty_a_reused_target_without_dropping_it()
        {
            var host = Environment.GetEnvironmentVariable("Whisparr__Postgres__Host");

            if (string.IsNullOrWhiteSpace(host))
            {
                Assert.Ignore("Whisparr__Postgres__Host is not set");
            }

            var options = new PostgresOptions
            {
                Host = host,
                Port = int.TryParse(Environment.GetEnvironmentVariable("Whisparr__Postgres__Port"), out var port) ? port : 5432,
                User = Environment.GetEnvironmentVariable("Whisparr__Postgres__User"),
                Password = Environment.GetEnvironmentVariable("Whisparr__Postgres__Password")
            };

            var server = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = "postgres",
                Pooling = false
            };

            var reused = "setup_reused_" + Guid.NewGuid().ToString("N");

            using (var connection = new NpgsqlConnection(server.ConnectionString))
            {
                connection.Open();
                connection.Execute($"CREATE DATABASE \"{reused}\"");
            }

            try
            {
                var state = GivenState(options);
                state.Begin(false);
                state.RecordReusedEmptyTarget(reused);

                // What phase 1 wrote before it failed.
                server.Database = reused;

                using (var connection = new NpgsqlConnection(server.ConnectionString))
                {
                    connection.Open();
                    connection.Execute("CREATE TABLE \"Written\" (\"Id\" SERIAL PRIMARY KEY); CREATE VIEW \"WrittenView\" AS SELECT 1 AS x; INSERT INTO \"Written\" DEFAULT VALUES");
                }

                GivenState(options).DiscardIfPresent().Should().BeTrue();

                File.Exists(Path5("v5-setup.json")).Should().BeFalse();

                using (var connection = new NpgsqlConnection(server.ConnectionString))
                {
                    connection.Open();
                    connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public'").Should().Be(0);

                    // Usable again, as the builder's own emptiness check and a retry need it to be.
                    connection.Execute("CREATE TABLE \"Again\" (\"Id\" INT)");
                }
            }
            finally
            {
                server.Database = "postgres";

                using var connection = new NpgsqlConnection(server.ConnectionString);
                connection.Open();
                connection.Execute($"DROP DATABASE IF EXISTS \"{reused}\" WITH (FORCE)");
            }
        }

        [Test]
        public void discard_should_restore_the_users_config5_byte_for_byte()
        {
            var original = new byte[] { 0xEF, 0xBB, 0xBF, 0x3C, 0x43, 0x6F, 0x6E, 0x66, 0x69, 0x67, 0x2F, 0x3E, 0x0D, 0x0A, 0x00, 0xFF };
            var config5 = Path5("config5.xml");
            var copy = config5 + ".pre-v5-setup";

            File.WriteAllBytes(config5, original);
            File.Copy(config5, copy);

            var state = GivenState();
            state.Begin(false);
            state.RecordConfig5Backup(copy);

            // What the seeder wrote over it.
            File.WriteAllText(config5, "<Config><PostgresMainDb>whisparr5-main</PostgresMainDb></Config>");

            GivenState().DiscardIfPresent().Should().BeTrue();

            File.ReadAllBytes(config5).Should().Equal(original);
            File.Exists(copy).Should().BeFalse();
            File.Exists(Path5("v5-setup.json")).Should().BeFalse();
        }

        [Test]
        public void complete_should_remove_the_config5_copy()
        {
            var config5 = Path5("config5.xml");
            var copy = config5 + ".pre-v5-setup";

            File.WriteAllText(config5, "<Config>seeded</Config>");
            File.WriteAllText(copy, "<Config>users</Config>");

            var state = GivenState();
            state.Begin(false);
            state.RecordConfig5Backup(copy);

            state.Complete();

            File.Exists(copy).Should().BeFalse();
            File.Exists(Path5("v5-setup.json")).Should().BeFalse();
            File.ReadAllText(config5).Should().Be("<Config>seeded</Config>");
        }

        private string GivenSqliteV5(bool withStateRow)
        {
            var path = Path5("whisparr5.db");

            using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
            connection.Open();
            connection.Execute("CREATE TABLE \"LegacyMigrationState\" (\"Id\" INTEGER PRIMARY KEY, \"Phase\" TEXT)");

            if (withStateRow)
            {
                connection.Execute("INSERT INTO \"LegacyMigrationState\" (\"Phase\") VALUES ('Done')");
            }

            return path;
        }

        [Test]
        public void discard_should_keep_a_finished_sqlite_database_when_the_sidecar_was_left_behind()
        {
            var v5 = GivenSqliteV5(true);
            var copy = Path5("config5.xml.pre-v5-setup");

            File.WriteAllText(Path5("config5.xml"), "<Config>seeded</Config>");
            File.WriteAllText(copy, "<Config>users</Config>");

            var state = GivenState();
            state.Begin(true);
            state.RecordSqlitePartial(Path5("whisparr5.db.partial"));
            state.RecordConfig5Backup(copy);

            GivenState().DiscardIfPresent().Should().BeFalse();

            File.Exists(v5).Should().BeTrue();
            File.ReadAllText(Path5("config5.xml")).Should().Be("<Config>seeded</Config>");
            File.Exists(copy).Should().BeFalse();
            File.Exists(Path5("v5-setup.json")).Should().BeFalse();
        }

        [Test]
        public void discard_should_still_undo_a_sqlite_database_without_a_state_row()
        {
            var v5 = GivenSqliteV5(false);
            var partial = Path5("whisparr5.db.partial");

            File.WriteAllText(partial, "x");
            File.WriteAllText(Path5("config5.xml"), "<Config />");

            var state = GivenState();
            state.Begin(true);
            state.RecordSqlitePartial(partial);

            GivenState().DiscardIfPresent().Should().BeTrue();

            File.Exists(partial).Should().BeFalse();
            File.Exists(Path5("config5.xml")).Should().BeFalse();

            // Not recorded as built by this run, so it is not touched.
            File.Exists(v5).Should().BeTrue();
        }

        [Test]
        public void discard_should_keep_a_finished_postgres_database_when_the_sidecar_was_left_behind()
        {
            var host = Environment.GetEnvironmentVariable("Whisparr__Postgres__Host");

            if (string.IsNullOrWhiteSpace(host))
            {
                Assert.Ignore("Whisparr__Postgres__Host is not set");
            }

            var options = new PostgresOptions
            {
                Host = host,
                Port = int.TryParse(Environment.GetEnvironmentVariable("Whisparr__Postgres__Port"), out var port) ? port : 5432,
                User = Environment.GetEnvironmentVariable("Whisparr__Postgres__User"),
                Password = Environment.GetEnvironmentVariable("Whisparr__Postgres__Password")
            };

            var server = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = "postgres",
                Pooling = false
            };

            var finished = "setup_finished_" + Guid.NewGuid().ToString("N");

            using (var connection = new NpgsqlConnection(server.ConnectionString))
            {
                connection.Open();
                connection.Execute($"CREATE DATABASE \"{finished}\"");
            }

            try
            {
                server.Database = finished;

                using (var connection = new NpgsqlConnection(server.ConnectionString))
                {
                    connection.Open();
                    connection.Execute("CREATE TABLE \"LegacyMigrationState\" (\"Id\" SERIAL PRIMARY KEY, \"Phase\" TEXT); INSERT INTO \"LegacyMigrationState\" (\"Phase\") VALUES ('Done')");
                }

                File.WriteAllText(Path5("config5.xml"), "<Config>seeded</Config>");

                var state = GivenState(options);
                state.Begin(true);
                state.RecordDatabase(finished);

                GivenState(options).DiscardIfPresent().Should().BeFalse();

                File.Exists(Path5("config5.xml")).Should().BeTrue();
                File.Exists(Path5("v5-setup.json")).Should().BeFalse();

                server.Database = "postgres";

                using var check = new NpgsqlConnection(server.ConnectionString);
                check.Open();
                check.ExecuteScalar<int>("SELECT COUNT(*) FROM pg_database WHERE datname = @finished", new { finished }).Should().Be(1);
            }
            finally
            {
                server.Database = "postgres";

                using var connection = new NpgsqlConnection(server.ConnectionString);
                connection.Open();
                connection.Execute($"DROP DATABASE IF EXISTS \"{finished}\" WITH (FORCE)");
            }
        }

        [Test]
        public void discard_should_keep_a_reused_target_recorded_when_it_cannot_be_emptied()
        {
            var unreachable = new PostgresOptions { Host = "127.0.0.1", Port = 1, User = "u", Password = "p" };

            var state = GivenState(unreachable);
            state.Begin(false);
            state.RecordReusedEmptyTarget("stuck_reused");

            GivenState(unreachable).DiscardIfPresent().Should().BeTrue();

            File.ReadAllText(Path5("v5-setup.json")).Should().Contain("stuck_reused");
        }

        [Test]
        public void discard_should_drop_recorded_db_and_leave_others()
        {
            var host = Environment.GetEnvironmentVariable("Whisparr__Postgres__Host");

            if (string.IsNullOrWhiteSpace(host))
            {
                Assert.Ignore("Whisparr__Postgres__Host is not set");
            }

            var options = new PostgresOptions
            {
                Host = host,
                Port = int.TryParse(Environment.GetEnvironmentVariable("Whisparr__Postgres__Port"), out var port) ? port : 5432,
                User = Environment.GetEnvironmentVariable("Whisparr__Postgres__User"),
                Password = Environment.GetEnvironmentVariable("Whisparr__Postgres__Password")
            };

            var server = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = "postgres",
                Pooling = false
            }.ConnectionString;

            var recorded = "setup_recorded_" + Guid.NewGuid().ToString("N");
            var other = "setup_other_" + Guid.NewGuid().ToString("N");

            using (var connection = new NpgsqlConnection(server))
            {
                connection.Open();
                connection.Execute($"CREATE DATABASE \"{recorded}\"");
                connection.Execute($"CREATE DATABASE \"{other}\"");
            }

            try
            {
                var state = GivenState(options);
                state.Begin(false);
                state.RecordDatabase(recorded);

                GivenState(options).DiscardIfPresent().Should().BeTrue();

                using var connection = new NpgsqlConnection(server);
                connection.Open();
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pg_database WHERE datname = @recorded", new { recorded }).Should().Be(0);
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM pg_database WHERE datname = @other", new { other }).Should().Be(1);
            }
            finally
            {
                using var connection = new NpgsqlConnection(server);
                connection.Open();
                connection.Execute($"DROP DATABASE IF EXISTS \"{recorded}\"");
                connection.Execute($"DROP DATABASE IF EXISTS \"{other}\"");
            }
        }
    }
}
