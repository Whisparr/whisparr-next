using System;
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
