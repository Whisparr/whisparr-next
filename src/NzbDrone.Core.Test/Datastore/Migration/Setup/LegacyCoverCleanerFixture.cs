using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class LegacyCoverCleanerFixture : TestBase
    {
        private string _coverRoot;
        private LegacyCoverCleaner _cleaner;
        private SQLiteConnection _connection;

        [SetUp]
        public void Setup()
        {
            var appData = Path.Combine(TempFolder, "appdata");
            _coverRoot = Path.Combine(appData, "MediaCover");

            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.AppDataFolder).Returns(appData);

            _cleaner = new LegacyCoverCleaner(folders.Object);

            _connection = new SQLiteConnection("Data Source=:memory:");
            _connection.Open();
            _connection.Execute("CREATE TABLE \"Movies\" (\"Id\" INTEGER)");
            _connection.Execute("CREATE TABLE \"Performers\" (\"Id\" INTEGER)");
            _connection.Execute("CREATE TABLE \"Studios\" (\"Id\" INTEGER)");
            _connection.Execute("CREATE TABLE \"LegacyMigrationReportItems\" (\"Category\" INTEGER, \"Area\" TEXT, \"Subject\" TEXT, \"Detail\" TEXT, \"Count\" INTEGER)");
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
        }

        private string GivenCover(string kind, string folder, int bytes = 2048)
        {
            var path = Path.Combine(_coverRoot, kind, folder);

            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "poster.jpg"), new byte[bytes]);

            return path;
        }

        private void GivenRow(string table, int id)
        {
            _connection.Execute($"INSERT INTO \"{table}\" VALUES ({id})");
        }

        [Test]
        public void should_keep_covers_of_rows_that_kept_their_id()
        {
            var movie = GivenCover("movie", "1");
            var performer = GivenCover("performer", "2");
            var studio = GivenCover("studio", "3");
            GivenRow("Movies", 1);
            GivenRow("Performers", 2);
            GivenRow("Studios", 3);

            var cleanup = _cleaner.Prepare(_connection);
            _cleaner.Archive(cleanup);

            cleanup.Moves.Should().BeEmpty();
            Directory.Exists(movie).Should().BeTrue();
            Directory.Exists(performer).Should().BeTrue();
            Directory.Exists(studio).Should().BeTrue();
            Directory.Exists(cleanup.ArchivePath).Should().BeFalse();
            _connection.ExecuteScalar<long>("SELECT COUNT(*) FROM \"LegacyMigrationReportItems\"").Should().Be(0);
        }

        [Test]
        public void should_move_covers_of_rows_that_are_gone_keeping_the_layout()
        {
            var movie = GivenCover("movie", "500");
            var studio = GivenCover("studio", "500");

            var cleanup = _cleaner.Prepare(_connection);
            _cleaner.Archive(cleanup);

            Directory.Exists(movie).Should().BeFalse();
            Directory.Exists(studio).Should().BeFalse();
            File.Exists(Path.Combine(cleanup.ArchivePath, "movie", "500", "poster.jpg")).Should().BeTrue();
            File.Exists(Path.Combine(cleanup.ArchivePath, "studio", "500", "poster.jpg")).Should().BeTrue();
        }

        [Test]
        public void should_not_touch_covers_until_archived()
        {
            var movie = GivenCover("movie", "500");

            _cleaner.Prepare(_connection);

            Directory.Exists(movie).Should().BeTrue();
        }

        [Test]
        public void should_report_the_archive_path_and_total_size_as_left_behind()
        {
            GivenCover("movie", "500", 1024);
            GivenCover("studio", "500", 1024);

            var cleanup = _cleaner.Prepare(_connection);

            var item = _connection.QuerySingle("SELECT * FROM \"LegacyMigrationReportItems\"");

            ((long)item.Category).Should().Be((long)LegacyReportCategory.LeftBehind);
            ((string)item.Subject).Should().Be(cleanup.ArchivePath);
            ((string)item.Detail).Should().Contain("2.0 KB");
            ((long)item.Count).Should().Be(2);
        }

        [Test]
        public void should_judge_each_kind_by_its_own_ids()
        {
            GivenCover("movie", "7");
            var studio = GivenCover("studio", "7");
            GivenRow("Movies", 7);

            var cleanup = _cleaner.Prepare(_connection);

            cleanup.Moves.Select(m => m.Source).Should().Equal(studio);
        }

        [Test]
        public void should_leave_anything_not_named_by_an_id()
        {
            GivenCover("movie", "keep-me");
            Directory.CreateDirectory(Path.Combine(_coverRoot, "12"));

            _cleaner.Prepare(_connection).Moves.Should().BeEmpty();
        }

        [Test]
        public void should_do_nothing_without_a_cover_folder()
        {
            var cleanup = _cleaner.Prepare(_connection);

            _cleaner.Invoking(c => c.Archive(cleanup)).Should().NotThrow();
        }
    }
}
