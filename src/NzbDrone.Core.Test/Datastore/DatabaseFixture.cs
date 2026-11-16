using System;
using System.Data.SQLite;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    public class DatabaseFixture : DbTest
    {
        [Test]
        public void SingleOrDefault_should_return_null_on_empty_db()
        {
            Mocker.Resolve<IDatabase>()
                .OpenConnection().Query<Movie>("SELECT * FROM \"Movies\"")
                .SingleOrDefault()
                .Should()
                .BeNull();
        }

        [Test]
        public void vacuum()
        {
            Mocker.Resolve<IDatabase>().Vacuum();
        }

        [TestCase(500, true)]
        [TestCase(5, false)]
        public void sqlite_vacuum_should_only_run_when_there_are_enough_free_pages(int deletedRows, bool vacuumed)
        {
            var path = GetTempFilePath();

            using (var conn = new SQLiteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();
                conn.Execute("CREATE TABLE \"Rows\" (\"Id\" INTEGER PRIMARY KEY, \"Data\" TEXT)");
                conn.Execute("INSERT INTO \"Rows\" (\"Data\") VALUES (@Data)", Enumerable.Range(0, 1000).Select(_ => new { Data = new string('x', 4000) }));
                conn.Execute("DELETE FROM \"Rows\" WHERE \"Id\" <= @Count", new { Count = deletedRows });
                conn.ExecuteScalar<long>("PRAGMA freelist_count;").Should().BeGreaterThan(0);
            }

            new Database("Test", () =>
            {
                var conn = new SQLiteConnection($"Data Source={path};Pooling=False");
                conn.Open();
                return conn;
            }).Vacuum();

            using (var conn = new SQLiteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();
                (conn.ExecuteScalar<long>("PRAGMA freelist_count;") == 0).Should().Be(vacuumed);
            }
        }

        [Test]
        public void postgres_should_not_contain_timestamp_without_timezone_columns()
        {
            if (Db.DatabaseType != DatabaseType.PostgreSQL)
            {
                return;
            }

            Mocker.Resolve<IDatabase>()
                .OpenConnection().Query("SELECT table_name, column_name, data_type FROM INFORMATION_SCHEMA.COLUMNS WHERE table_schema = 'public' AND data_type = 'timestamp without time zone'")
                .Should()
                .BeNullOrEmpty();
        }

        [Test]
        public void get_version()
        {
            Mocker.Resolve<IDatabase>().Version.Should().BeGreaterThan(new Version("3.0.0"));
        }
    }
}
