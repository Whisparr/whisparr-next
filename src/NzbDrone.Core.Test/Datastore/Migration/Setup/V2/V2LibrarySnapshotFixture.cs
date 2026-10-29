using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    [TestFixture]
    public class V2LibrarySnapshotFixture : V2PorterTestBase
    {
        private readonly V2LibrarySnapshot _subject = new ();

        private static object SeriesRow(int id, int tvdbId, string title, string path, bool monitored = true)
        {
            return new
            {
                Id = id,
                TvdbId = tvdbId,
                Title = title,
                CleanTitle = title.ToLowerInvariant(),
                Status = 0,
                Images = "[]",
                Path = path,
                Monitored = monitored,
                QualityProfileId = 2,
                Runtime = 0,
                UseSceneNumbering = false,
                OriginalLanguage = 1,
                MonitorNewItems = 1,
                SeriesType = 0,
                Tags = "[3]",
                Added = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified)
            };
        }

        private static object EpisodeRow(int id, int seriesId, int tvdbId, string title, bool monitored, int fileId)
        {
            return new
            {
                Id = id,
                SeriesId = seriesId,
                SeasonNumber = 1,
                Runtime = 0,
                TvdbId = tvdbId,
                ExternalId = "ext-" + id,
                Title = title,
                AirDate = "2020-01-02",
                Monitored = monitored,
                EpisodeFileId = fileId
            };
        }

        private static object FileRow(int id, int seriesId, string relativePath, string quality, string languages, int flags)
        {
            return new
            {
                Id = id,
                SeriesId = seriesId,
                RelativePath = relativePath,
                Size = 123456789012L,
                Quality = quality,
                Languages = languages,
                ReleaseGroup = "GRP",
                SceneName = "scene.name",
                OriginalFilePath = "/dl/scene.name.mp4",
                MediaInfo = "{\"a\":1}",
                IndexerFlags = flags,
                SeasonNumber = 1,
                DateAdded = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)
            };
        }

        private List<IDictionary<string, object>> V5Rows(string table)
        {
            return V5.Query($"SELECT * FROM \"{table}\" ORDER BY \"Id\"", transaction: Transaction).Cast<IDictionary<string, object>>().ToList();
        }

        private void Snapshot(out V2SnapshotCounts counts)
        {
            counts = _subject.Snapshot(OpenContext());
        }

        private static readonly DateTime Now = new (2024, 6, 7, 8, 9, 10, DateTimeKind.Unspecified);

        // PostgreSQL Ids are GENERATED ALWAYS, so the fixture rows need the override to keep their Ids.
        private static void InsertPg(NpgsqlConnection connection, string table, object values)
        {
            var properties = values.GetType().GetProperties();
            var columns = string.Join(", ", properties.Select(p => $"\"{p.Name}\""));
            var parameters = string.Join(", ", properties.Select(p => "@" + p.Name));

            connection.Execute($"INSERT INTO \"{table}\" ({columns}) OVERRIDING SYSTEM VALUE VALUES ({parameters})", values);
        }

        [Test]
        public void should_snapshot_series_episodes_files_with_ids_and_monitored()
        {
            InsertV2("RootFolders", new { Id = 1, Path = "/data" });
            InsertV2("Series", SeriesRow(7, 4242, "Studio A", "/data/Studio A", false));
            InsertV2("Series", SeriesRow(8, 4343, "Studio B", "/data/Studio B"));
            InsertV2("Episodes", EpisodeRow(70, 7, 999, "Scene One", true, 5));
            InsertV2("Episodes", EpisodeRow(71, 7, 1000, "Scene Two", false, 0));
            InsertV2("EpisodeFiles", FileRow(5, 7, "Season 1/one.mp4", "{\"quality\":7,\"revision\":{\"version\":1}}", "[1]", 0));

            Snapshot(out var counts);

            counts.Series.Should().Be(2);
            counts.Episodes.Should().Be(2);
            counts.EpisodeFiles.Should().Be(1);

            var series = V5Rows("LegacyV2Series");

            series.Select(s => Convert.ToInt32(s["LegacyId"])).Should().Equal(7, 8);
            series[0]["Id"].Should().NotBe(7);
            Convert.ToInt32(series[0]["TpdbSiteId"]).Should().Be(4242);
            series[0]["Title"].Should().Be("Studio A");
            series[0]["Path"].Should().Be("/data/Studio A");
            series[0]["RootFolderPath"].Should().Be("/data");
            V2QualityPorter.Bool(series[0], "Monitored").Should().BeFalse();
            V2QualityPorter.Bool(series[1], "Monitored").Should().BeTrue();
            V2QualityPorter.Bool(series[0], "Done").Should().BeFalse();
            Convert.ToInt32(series[0]["MonitorNewItems"]).Should().Be(1);
            Convert.ToInt32(series[0]["QualityProfileId"]).Should().Be(2);
            series[0]["Tags"].Should().Be("[3]");
            Convert.ToInt32(series[0]["SeriesType"]).Should().Be(0);
            series[0]["Added"].Should().NotBeNull();

            var episodes = V5Rows("LegacyV2Episodes");

            episodes.Select(e => Convert.ToInt32(e["LegacyId"])).Should().Equal(70, 71);
            Convert.ToInt32(episodes[0]["LegacySeriesId"]).Should().Be(7);
            Convert.ToInt32(episodes[0]["TpdbSceneId"]).Should().Be(999);
            episodes[0]["ExternalId"].Should().Be("ext-70");
            episodes[0]["Title"].Should().Be("Scene One");
            episodes[0]["AirDate"].Should().Be("2020-01-02");
            V2QualityPorter.Bool(episodes[0], "Monitored").Should().BeTrue();
            V2QualityPorter.Bool(episodes[1], "Monitored").Should().BeFalse();
            Convert.ToInt32(episodes[0]["LegacyEpisodeFileId"]).Should().Be(5);
            Convert.ToInt32(episodes[1]["LegacyEpisodeFileId"]).Should().Be(0);

            var files = V5Rows("LegacyV2EpisodeFiles");

            files.Should().HaveCount(1);
            Convert.ToInt32(files[0]["LegacyId"]).Should().Be(5);
            Convert.ToInt32(files[0]["LegacySeriesId"]).Should().Be(7);
            files[0]["RelativePath"].Should().Be("Season 1/one.mp4");
            files[0]["Path"].Should().Be("/data/Studio A/Season 1/one.mp4");
            Convert.ToInt64(files[0]["Size"]).Should().Be(123456789012L);
            files[0]["ReleaseGroup"].Should().Be("GRP");
            files[0]["SceneName"].Should().Be("scene.name");
            files[0]["OriginalFilePath"].Should().Be("/dl/scene.name.mp4");
            files[0]["MediaInfo"].Should().Be("{\"a\":1}");
            files[0]["DateAdded"].Should().NotBeNull();
        }

        [Test]
        public void should_resolve_root_folder_by_longest_prefix()
        {
            InsertV2("RootFolders", new { Id = 1, Path = "/data" });
            InsertV2("RootFolders", new { Id = 2, Path = "/data/studios" });
            InsertV2("Series", SeriesRow(1, 1, "X", "/data/studios/X"));

            Snapshot(out _);

            V5Rows("LegacyV2Series")[0]["RootFolderPath"].Should().Be("/data/studios");
            ReportFor(LegacyReportCategory.NeedsAttention).Should().BeEmpty();
        }

        [Test]
        public void should_not_match_root_folder_on_partial_segment()
        {
            InsertV2("RootFolders", new { Id = 1, Path = "/data/stud" });
            InsertV2("Series", SeriesRow(1, 1, "X", "/data/studios/X"));

            Snapshot(out _);

            V5Rows("LegacyV2Series")[0]["RootFolderPath"].Should().BeNull();

            var attention = ReportFor(LegacyReportCategory.NeedsAttention);

            attention.Should().HaveCount(1);
            attention[0].Area.Should().Be("Library");
            attention[0].Subject.Should().Be("X");
            attention[0].Detail.Should().Be("/data/studios/X");
        }

        [Test]
        public void should_remap_file_quality_languages_and_flags()
        {
            InsertV2("Series", SeriesRow(1, 1, "X", "/data/X"));
            InsertV2("EpisodeFiles", FileRow(1, 1, "a.mp4", "{\"quality\":13,\"revision\":{\"version\":2,\"real\":0}}", "[1,47]", 8 | 16 | 256));
            InsertV2("EpisodeFiles", FileRow(2, 1, "b.mp4", "{\"quality\":{\"id\":23,\"name\":\"Bluray-576p\"},\"revision\":{\"version\":1}}", "[{\"id\":1,\"name\":\"English\"},{\"id\":47,\"name\":\"Azerbaijani\"}]", 0));
            InsertV2("EpisodeFiles", FileRow(3, 1, "c.mp4", "{\"quality\":7,\"revision\":{\"version\":1}}", "[]", 0));

            Snapshot(out _);

            var files = V5Rows("LegacyV2EpisodeFiles");
            var english = V2QualityMap.Language(1);

            english.Should().NotBeNull();

            var first = JsonNode.Parse((string)files[0]["Quality"]);

            first["quality"].GetValue<int>().Should().Be(20);
            first["revision"]["version"].GetValue<int>().Should().Be(2);
            JsonNode.Parse((string)files[0]["Languages"]).AsArray().Select(n => n.GetValue<int>()).Should().Equal(english.Value);
            Convert.ToInt32(files[0]["IndexerFlags"]).Should().Be(32 | 128);

            var second = JsonNode.Parse((string)files[1]["Quality"]);

            second["quality"]["id"].GetValue<int>().Should().Be(21);
            second["quality"]["name"].GetValue<string>().Should().Be("Bluray-576p");

            var languages = JsonNode.Parse((string)files[1]["Languages"]).AsArray();

            languages.Should().HaveCount(1);
            languages[0]["id"].GetValue<int>().Should().Be(english.Value);
            languages[0]["name"].GetValue<string>().Should().Be("English");

            JsonNode.Parse((string)files[2]["Quality"])["quality"].GetValue<int>().Should().Be(7);
            JsonNode.Parse((string)files[2]["Languages"]).AsArray().Should().BeEmpty();
        }

        [Test]
        public void should_page_large_tables()
        {
            InsertV2("Series", SeriesRow(1, 1, "X", "/data/X"));
            ExecuteV2("INSERT INTO \"Episodes\" (\"Monitored\", \"SeriesId\", \"SeasonNumber\", \"Runtime\", \"Title\") WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 2500) SELECT 1, 1, 1, 0, 'E' || x FROM c");

            Snapshot(out var counts);

            counts.Episodes.Should().Be(2500);
            V5.ExecuteScalar<int>("SELECT COUNT(*) FROM \"LegacyV2Episodes\"", transaction: Transaction).Should().Be(2500);
            V5.ExecuteScalar<int>("SELECT COUNT(DISTINCT \"LegacyId\") FROM \"LegacyV2Episodes\"", transaction: Transaction).Should().Be(2500);
        }

        [Test]
        public void should_count_left_behind_tables()
        {
            for (var i = 0; i < 3; i++)
            {
                InsertV2("History", new { EpisodeId = 1, SeriesId = 1, SourceTitle = "t" + i, Date = Now, Quality = "{}", Data = "{}" });
            }

            OpenContext();
            _subject.CountLeftBehind(Context);

            var rows = ReportFor(LegacyReportCategory.LeftBehind);

            rows.Select(r => r.Area).Should().BeEquivalentTo("History", "ScheduledTasks");
            rows.Single(r => r.Area == "History").Count.Should().Be(3);
            rows.Single(r => r.Area == "ScheduledTasks").Count.Should().Be(10);
        }

        [Test]
        public void should_count_status_tables_and_skip_missing_ones()
        {
            ExecuteV2("INSERT INTO \"IndexerStatus\" (\"ProviderId\", \"EscalationLevel\") VALUES (1, 0)");

            OpenContext();
            _subject.CountLeftBehind(Context);

            var areas = ReportFor(LegacyReportCategory.LeftBehind).Select(r => r.Area).ToList();

            areas.Should().BeEquivalentTo("IndexerStatus", "ScheduledTasks");
        }

        [Test]
        public void should_snapshot_through_the_porter()
        {
            InsertV2("Series", SeriesRow(1, 1, "X", "/data/X"));

            var context = OpenContext();
            var counts = NewPorter().SnapshotLibrary(context);

            counts.Series.Should().Be(1);
            ReportFor(LegacyReportCategory.LeftBehind).Should().Contain(r => r.Area == "ScheduledTasks");
        }

        [Test]
        public void should_snapshot_from_postgres_legacy()
        {
            var host = Environment.GetEnvironmentVariable("Whisparr__Postgres__Host");

            if (string.IsNullOrWhiteSpace(host))
            {
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            var admin = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.TryParse(Environment.GetEnvironmentVariable("Whisparr__Postgres__Port"), out var port) ? port : 5432,
                Username = Environment.GetEnvironmentVariable("Whisparr__Postgres__User"),
                Password = Environment.GetEnvironmentVariable("Whisparr__Postgres__Password"),
                Database = "postgres",
                Pooling = false
            };

            var legacyDb = "legacy_v2_t6_" + Guid.NewGuid().ToString("N");
            var legacy = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = legacyDb };

            using (var connection = new NpgsqlConnection(admin.ConnectionString))
            {
                connection.Open();
                connection.Execute($"CREATE DATABASE \"{legacyDb}\"");
            }

            try
            {
                LegacyDbBuilder.CreateV2PostgresFromDump(legacy.ConnectionString);

                using (var connection = new NpgsqlConnection(legacy.ConnectionString))
                {
                    connection.Open();

                    InsertPg(connection, "RootFolders", new { Id = 1, Path = "/data" });
                    InsertPg(connection, "Series", SeriesRow(7, 4242, "Studio A", "/data/Studio A"));
                    InsertPg(connection, "Episodes", EpisodeRow(70, 7, 999, "Scene One", true, 5));
                    InsertPg(connection, "EpisodeFiles", FileRow(5, 7, "one.mp4", "{\"quality\":13,\"revision\":{\"version\":1}}", "[1]", 8));
                    InsertPg(connection, "History", new { Id = 1, EpisodeId = 1, SeriesId = 1, SourceTitle = "t", Date = Now, Quality = "{}", Data = "{}" });
                }

                var install = new LegacyInstall
                {
                    Kind = LegacyKind.V2,
                    Database = new LegacyDatabaseLocation { Type = DatabaseType.PostgreSQL, PostgresConnectionString = legacy.ConnectionString }
                };

                Transaction = V5.BeginTransaction();
                var context = NewPorter().Open(install, V5, Transaction, V5Type);
                SetContext(context);

                var counts = NewPorter().SnapshotLibrary(context);
                Commit();
                Transaction = null;

                counts.Series.Should().Be(1);
                counts.Episodes.Should().Be(1);
                counts.EpisodeFiles.Should().Be(1);

                var series = V5.Query("SELECT * FROM \"LegacyV2Series\"").Cast<IDictionary<string, object>>().Single();

                Convert.ToInt32(series["LegacyId"]).Should().Be(7);
                series["RootFolderPath"].Should().Be("/data");
                V2QualityPorter.Bool(series, "Monitored").Should().BeTrue();

                var file = V5.Query("SELECT * FROM \"LegacyV2EpisodeFiles\"").Cast<IDictionary<string, object>>().Single();

                file["Path"].Should().Be("/data/Studio A/one.mp4");
                JsonNode.Parse((string)file["Quality"])["quality"].GetValue<int>().Should().Be(20);
                Convert.ToInt32(file["IndexerFlags"]).Should().Be(32);

                ReportFor(LegacyReportCategory.LeftBehind).Select(r => r.Area).Should().BeEquivalentTo("History", "ScheduledTasks");
            }
            finally
            {
                Context?.Dispose();

                using var connection = new NpgsqlConnection(admin.ConnectionString);
                connection.Open();
                connection.Execute($"DROP DATABASE IF EXISTS \"{legacyDb}\" WITH (FORCE)");
            }
        }
    }
}
