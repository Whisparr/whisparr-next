using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class SceneSourceSeederFixture : DbTest
    {
        private const int MovieItem = 0;
        private const int SceneItem = 1;

        private IDbConnection _connection;

        [SetUp]
        public void Setup()
        {
            _connection = Mocker.Resolve<IMainDatabase>().OpenConnection();

            _connection.Execute("INSERT INTO \"RootFolders\" (\"Path\") VALUES ('/library')");
        }

        [TearDown]
        public void Teardown()
        {
            _connection?.Dispose();
        }

        private SceneSeedResult Seed()
        {
            using var transaction = _connection.BeginTransaction();

            var result = new SceneSourceSeeder().Seed(_connection, transaction);

            transaction.Commit();

            return result;
        }

        // Returns the Movies id
        private int AddItem(int itemType, string foreignId, string title, string path = "/library/scenes/Scene", bool monitored = true, int qualityProfileId = 2, string tags = "[3]")
        {
            var metadataId = _connection.ExecuteScalar<int>(
                "INSERT INTO \"MovieMetadata\" (\"ForeignId\", \"MetadataSource\", \"ImdbId\", \"TmdbId\", \"Images\", \"Title\", \"OriginalLanguage\", \"Status\", \"Runtime\", \"Year\", \"Recommendations\", \"ItemType\", \"ReleaseDateUtc\", \"StudioForeignId\") " +
                "VALUES (@foreignId, 0, 'tt1', 0, '[]', @title, 1, 0, 0, 2021, '[]', @itemType, @released, 'studio-1') RETURNING \"Id\"",
                new { foreignId, title, itemType, released = new DateTime(2021, 3, 4, 0, 0, 0, DateTimeKind.Utc) });

            var movieId = _connection.ExecuteScalar<int>(
                "INSERT INTO \"Movies\" (\"Path\", \"Monitored\", \"QualityProfileId\", \"Tags\", \"MovieFileId\", \"MovieMetadataId\") " +
                "VALUES (@path, @monitored, @qualityProfileId, @tags, 0, @metadataId) RETURNING \"Id\"",
                new { path, monitored, qualityProfileId, tags, metadataId });

            _connection.Execute(
                "INSERT INTO \"Credits\" (\"MovieMetadataId\", \"PerformerForeignId\", \"Type\", \"Order\") VALUES (@metadataId, 'p1', 0, 0)",
                new { metadataId });

            _connection.Execute(
                "INSERT INTO \"AlternativeTitles\" (\"Title\", \"CleanTitle\", \"SourceType\", \"MovieMetadataId\") VALUES (@title, @clean, 0, @metadataId)",
                new { title = $"Alt {metadataId}", clean = $"alt{metadataId}", metadataId });

            return movieId;
        }

        private int AddFile(int movieId, string relativePath)
        {
            var fileId = _connection.ExecuteScalar<int>(
                "INSERT INTO \"MovieFiles\" (\"MovieId\", \"Quality\", \"Size\", \"DateAdded\", \"RelativePath\", \"OsHash\", \"PHash\", \"SceneName\", \"MediaInfo\", \"ReleaseGroup\", \"Languages\", \"IndexerFlags\", \"Edition\", \"OriginalFilePath\") " +
                "VALUES (@movieId, '{\"quality\":7}', 1234, @added, @relativePath, 'osh', 'ph', 'scene.name', '{\"a\":1}', 'GRP', '[{\"id\":1}]', 1, 'Uncut', 'orig/path.mp4') RETURNING \"Id\"",
                new { movieId, relativePath, added = new DateTime(2022, 5, 6, 0, 0, 0, DateTimeKind.Utc) });

            _connection.Execute("UPDATE \"Movies\" SET \"MovieFileId\" = @fileId WHERE \"Id\" = @movieId", new { movieId, fileId });

            return fileId;
        }

        private void AddStudio(string foreignId, string tpdbId)
        {
            _connection.Execute(
                "INSERT INTO \"Studios\" (\"ForeignId\", \"TpdbId\", \"QualityProfileId\", \"RootFolderPath\", \"SearchOnAdd\", \"Title\", \"CleanTitle\", \"Images\", \"Status\", \"TmdbId\", \"MovieCount\", \"SceneCount\", \"TotalMovieCount\", \"TotalSceneCount\", \"SizeOnDisk\", \"Monitored\", \"MoviesMonitored\", \"WhisparrMonitorNewItems\", \"Tags\") " +
                "VALUES (@foreignId, @tpdbId, 4, '/library', false, @title, @title, '[]', 0, 0, 0, 0, 0, 0, 0, true, true, true, '[5]')",
                new { foreignId, tpdbId, title = $"Studio {foreignId}" });
        }

        private void AddPerformer(string foreignId, string tpdbId, int visibility = 1)
        {
            _connection.Execute(
                "INSERT INTO \"Performers\" (\"ForeignId\", \"TpdbId\", \"QualityProfileId\", \"RootFolderPath\", \"SearchOnAdd\", \"Name\", \"CleanName\", \"Gender\", \"Status\", \"Images\", \"Monitored\", \"MoviesMonitored\", \"WhisparrMonitorNewItems\", \"Visibility\", \"Tags\") " +
                "VALUES (@foreignId, @tpdbId, 4, '/library', false, @name, @name, 0, 0, '[]', true, false, true, @visibility, '[6]')",
                new { foreignId, tpdbId, name = $"Performer {foreignId}", visibility });
        }

        private void AddExclusion(string foreignId, string type)
        {
            _connection.Execute(
                "INSERT INTO \"ImportExclusions\" (\"ForeignId\", \"MovieTitle\", \"Type\") VALUES (@foreignId, 'x', @type)",
                new { foreignId, type });
        }

        private int Count(string table)
        {
            return _connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{table}\"");
        }

        private List<ReportRow> Report()
        {
            return _connection.Query<ReportRow>("SELECT \"Category\", \"Area\", \"Subject\", \"Detail\", \"Count\" FROM \"LegacyMigrationReportItems\" ORDER BY \"Id\"").ToList();
        }

        [Test]
        public void scene_with_file_becomes_seed_and_import_file()
        {
            var movieId = AddItem(SceneItem, "scene-1", "Scene One");
            AddFile(movieId, "scene.mp4");

            var result = Seed();

            result.Scenes.Should().Be(1);
            result.Detached.Should().Be(1);
            result.Dropped.Should().Be(0);
            result.NeedsPhase2.Should().BeTrue();

            var seed = _connection.QuerySingle<SceneSeedRow>("SELECT * FROM \"LegacySceneSeeds\"");
            seed.LegacyForeignId.Should().Be("scene-1");
            seed.Title.Should().Be("Scene One");
            seed.ReleaseDate.Should().Be(new DateTime(2021, 3, 4, 0, 0, 0, DateTimeKind.Utc));
            seed.LegacyStudioForeignId.Should().Be("studio-1");
            seed.Monitored.Should().BeTrue();
            seed.QualityProfileId.Should().Be(2);
            seed.Path.Should().Be("/library/scenes/Scene");
            seed.RootFolderPath.Should().Be("/library");
            seed.Tags.Should().Be("[3]");
            seed.FileSize.Should().Be(1234);
            seed.FileQuality.Should().Be("{\"quality\":7}");
            seed.FileLanguages.Should().Be("[{\"id\":1}]");
            seed.FileMediaInfo.Should().Be("{\"a\":1}");
            seed.FileReleaseGroup.Should().Be("GRP");
            seed.FileSceneName.Should().Be("scene.name");
            seed.FileOriginalFilePath.Should().Be("orig/path.mp4");
            seed.FileIndexerFlags.Should().Be(1);
            seed.FileEdition.Should().Be("Uncut");
            seed.FileDateAdded.Should().Be(new DateTime(2022, 5, 6, 0, 0, 0, DateTimeKind.Utc));
            seed.Resolved.Should().BeFalse();

            var import = _connection.QuerySingle<(int Id, string Path, string OsHash)>("SELECT \"Id\", \"Path\", \"OsHash\" FROM \"ImportFiles\"");
            seed.ImportFileId.Should().Be(import.Id);
            import.Path.Should().Be("/library/scenes/Scene/scene.mp4");
            import.OsHash.Should().Be("osh");

            Count("MovieFiles").Should().Be(0);
            Count("Movies").Should().Be(0);
            Count("MovieMetadata").Should().Be(0);
            Count("Credits").Should().Be(0);
            Count("AlternativeTitles").Should().Be(0);
        }

        [Test]
        public void scene_without_file_becomes_seed_without_import_file()
        {
            AddItem(SceneItem, "scene-1", "Scene One", monitored: false);

            var result = Seed();

            result.Scenes.Should().Be(1);
            result.Detached.Should().Be(0);

            var seed = _connection.QuerySingle<SceneSeedRow>("SELECT * FROM \"LegacySceneSeeds\"");
            seed.ImportFileId.Should().BeNull();
            seed.Monitored.Should().BeFalse();
            seed.RootFolderPath.Should().Be("/library");
            seed.FileSize.Should().Be(0);
            seed.FileQuality.Should().BeNull();
            seed.FileDateAdded.Should().BeNull();

            Count("ImportFiles").Should().Be(0);
            Count("Movies").Should().Be(0);
        }

        [Test]
        public void scene_file_outside_root_is_dropped_and_reported()
        {
            var movieId = AddItem(SceneItem, "scene-1", "Scene One", path: "/elsewhere/Scene");
            AddFile(movieId, "scene.mp4");

            var result = Seed();

            result.Scenes.Should().Be(1);
            result.Detached.Should().Be(0);
            result.Dropped.Should().Be(1);

            var seed = _connection.QuerySingle<SceneSeedRow>("SELECT * FROM \"LegacySceneSeeds\"");
            seed.ImportFileId.Should().BeNull();
            seed.RootFolderPath.Should().BeNull();
            seed.FileSize.Should().Be(0);
            seed.FileQuality.Should().BeNull();

            Count("MovieFiles").Should().Be(0);
            Count("ImportFiles").Should().Be(0);

            var report = Report().Single(x => x.Category == 4);
            report.Area.Should().Be("Scenes");
            report.Subject.Should().Be("/elsewhere/Scene/scene.mp4");
            report.Detail.Should().Be("Outside every root folder; not linked");
            report.Count.Should().Be(1);
        }

        [Test]
        public void movies_are_untouched()
        {
            var movieId = AddItem(MovieItem, "movie-1", "A Movie", path: "/library/movies/A Movie");
            AddFile(movieId, "movie.mp4");
            AddItem(SceneItem, "scene-1", "Scene One");

            Seed();

            Count("Movies").Should().Be(1);
            Count("MovieMetadata").Should().Be(1);
            Count("MovieFiles").Should().Be(1);
            Count("Credits").Should().Be(1);
            Count("AlternativeTitles").Should().Be(1);
            Count("ImportFiles").Should().Be(0);
            _connection.ExecuteScalar<int>("SELECT \"MovieFileId\" FROM \"Movies\"").Should().BeGreaterThan(0);
            _connection.ExecuteScalar<string>("SELECT \"ForeignId\" FROM \"MovieMetadata\"").Should().Be("movie-1");
            Count("LegacySceneSeeds").Should().Be(1);
        }

        [Test]
        public void studios_and_visible_performers_become_seeds()
        {
            AddStudio("stash-s1", "tpdb-s1");
            AddStudio("stash-s2", null);
            AddPerformer("stash-p1", "tpdb-p1");
            AddPerformer("stash-p2", null, visibility: 0);

            var result = Seed();

            result.Studios.Should().Be(2);
            result.Performers.Should().Be(1);
            result.Scenes.Should().Be(0);
            result.NeedsPhase2.Should().BeTrue();

            var studios = _connection.Query<StudioSeedRow>("SELECT * FROM \"LegacyStudioSeeds\" ORDER BY \"Id\"").ToList();
            studios.Select(x => x.LegacyForeignId).Should().Equal("stash-s1", "stash-s2");
            studios[0].LegacyTpdbId.Should().Be("tpdb-s1");
            studios[1].LegacyTpdbId.Should().BeNull();
            studios[0].Title.Should().Be("Studio stash-s1");
            studios[0].Monitored.Should().BeTrue();
            studios[0].MoviesMonitored.Should().BeTrue();
            studios[0].WhisparrMonitorNewItems.Should().BeTrue();
            studios[0].RootFolderPath.Should().Be("/library");
            studios[0].QualityProfileId.Should().Be(4);
            studios[0].Tags.Should().Be("[5]");
            studios[0].Resolved.Should().BeFalse();

            var performers = _connection.Query<PerformerSeedRow>("SELECT * FROM \"LegacyPerformerSeeds\"").ToList();
            performers.Should().HaveCount(1);
            performers[0].LegacyForeignId.Should().Be("stash-p1");
            performers[0].LegacyTpdbId.Should().Be("tpdb-p1");
            performers[0].Name.Should().Be("Performer stash-p1");
            performers[0].Monitored.Should().BeTrue();
            performers[0].MoviesMonitored.Should().BeFalse();
            performers[0].Tags.Should().Be("[6]");

            Count("Studios").Should().Be(0);
            Count("Performers").Should().Be(0);
        }

        [Test]
        public void nothing_to_seed_does_not_need_phase_two()
        {
            var result = Seed();

            result.NeedsPhase2.Should().BeFalse();
            Report().Should().BeEmpty();
        }

        [Test]
        public void exclusions_rewritten_or_counted()
        {
            AddStudio("stash-s1", "tpdb-s1");
            AddStudio("stash-s2", null);
            AddPerformer("stash-p1", "tpdb-p1");
            AddExclusion("stash-s1", "Studio");
            AddExclusion("stash-s2", "Studio");
            AddExclusion("stash-p1", "Performer");
            AddExclusion("unknown-p", "Performer");
            AddExclusion("scene-x", "Scene");
            AddExclusion("scene-y", "Scene");
            AddExclusion("movie-x", "Movie");

            Seed();

            var remaining = _connection.Query<(string ForeignId, string Type)>("SELECT \"ForeignId\", \"Type\" FROM \"ImportExclusions\" ORDER BY \"Id\"").ToList();
            remaining.Select(x => x.ForeignId).Should().Equal("tpdb-s1", "tpdb-p1", "movie-x");

            var report = Report().Where(x => x.Area == "ImportExclusions").ToList();
            report.Should().HaveCount(3);
            report.Should().OnlyContain(x => x.Category == 2);
            report.Single(x => x.Subject == "Scene").Count.Should().Be(2);
            report.Single(x => x.Subject == "Studio").Count.Should().Be(1);
            report.Single(x => x.Subject == "Performer").Count.Should().Be(1);
        }

        [Test]
        public void exclusion_whose_tpdb_id_is_already_excluded_is_counted_not_duplicated()
        {
            AddStudio("stash-s1", "tpdb-s1");
            AddStudio("stash-s2", "tpdb-s1");
            AddExclusion("stash-s1", "Studio");
            AddExclusion("stash-s2", "Studio");

            Seed();

            _connection.Query<string>("SELECT \"ForeignId\" FROM \"ImportExclusions\"").Should().Equal("tpdb-s1");
            Report().Single(x => x.Subject == "Studio").Count.Should().Be(1);
        }

        [Test]
        public void exclusion_already_on_its_tpdb_id_is_kept()
        {
            AddStudio("tpdb-s1", "tpdb-s1");
            AddExclusion("tpdb-s1", "Studio");

            Seed();

            _connection.Query<string>("SELECT \"ForeignId\" FROM \"ImportExclusions\"").Should().Equal("tpdb-s1");
            Report().Should().BeEmpty();
        }

        [Test]
        public void import_list_movies_dropped_and_counted()
        {
            var metadataId = _connection.ExecuteScalar<int>(
                "INSERT INTO \"MovieMetadata\" (\"ForeignId\", \"MetadataSource\", \"ImdbId\", \"TmdbId\", \"Images\", \"Title\", \"OriginalLanguage\", \"Status\", \"Runtime\", \"Year\", \"Recommendations\", \"ItemType\") " +
                "VALUES ('listed', 0, 'tt9', 0, '[]', 'Listed', 1, 0, 0, 2020, '[]', 0) RETURNING \"Id\"");

            _connection.Execute("INSERT INTO \"ImportListMovies\" (\"ListId\", \"MovieMetadataId\") VALUES (1, @metadataId)", new { metadataId });
            _connection.Execute("INSERT INTO \"ImportListMovies\" (\"ListId\", \"MovieMetadataId\") VALUES (1, @metadataId)", new { metadataId });

            Seed();

            Count("ImportListMovies").Should().Be(0);

            var report = Report().Single(x => x.Area == "ImportListMovies");
            report.Category.Should().Be(2);
            report.Count.Should().Be(2);
        }

        [Test]
        public void left_behind_counts_only_scene_rows()
        {
            var scene = AddItem(SceneItem, "scene-1", "Scene One");
            var movie = AddItem(MovieItem, "movie-1", "A Movie", path: "/library/movies/A Movie");

            foreach (var (movieId, copies) in new[] { (scene, 2), (movie, 3) })
            {
                for (var i = 0; i < copies; i++)
                {
                    _connection.Execute(
                        "INSERT INTO \"ExtraFiles\" (\"MovieId\", \"MovieFileId\", \"RelativePath\", \"Extension\", \"Added\", \"LastUpdated\") VALUES (@movieId, 0, 'a', '.srt', @now, @now)",
                        new { movieId, now = DateTime.UtcNow });
                }
            }

            _connection.Execute(
                "INSERT INTO \"History\" (\"MovieId\", \"SourceTitle\", \"Date\", \"Quality\", \"Data\", \"EventType\") VALUES (@scene, 's', @now, '{}', '{}', 1)",
                new { scene, now = DateTime.UtcNow });

            _connection.Execute(
                "INSERT INTO \"Blocklist\" (\"SourceTitle\", \"Quality\", \"Date\", \"MovieId\") VALUES ('s', '{}', @now, @movie)",
                new { movie, now = DateTime.UtcNow });

            Seed();

            // The rows themselves stay
            Count("ExtraFiles").Should().Be(5);
            Count("History").Should().Be(1);
            Count("Blocklist").Should().Be(1);

            var report = Report().Where(x => x.Category == 2).ToList();
            report.Single(x => x.Area == "ExtraFiles").Count.Should().Be(2);
            report.Single(x => x.Area == "History").Count.Should().Be(1);
            report.Select(x => x.Area).Should().NotContain(new[] { "Blocklist", "SubtitleFiles", "MetadataFiles" });
        }

        [Test]
        public void scene_metadata_without_a_movie_row_is_removed_too()
        {
            _connection.Execute(
                "INSERT INTO \"MovieMetadata\" (\"ForeignId\", \"MetadataSource\", \"ImdbId\", \"TmdbId\", \"Images\", \"Title\", \"OriginalLanguage\", \"Status\", \"Runtime\", \"Year\", \"Recommendations\", \"ItemType\") " +
                "VALUES ('loose', 0, 'tt8', 0, '[]', 'Loose', 1, 0, 0, 2020, '[]', 1)");

            var result = Seed();

            result.Scenes.Should().Be(0);
            Count("MovieMetadata").Should().Be(0);
        }

        private class ReportRow
        {
            public int Category { get; set; }
            public string Area { get; set; }
            public string Subject { get; set; }
            public string Detail { get; set; }
            public int Count { get; set; }
        }

        private class SceneSeedRow
        {
            public string LegacyForeignId { get; set; }
            public string Title { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public string LegacyStudioForeignId { get; set; }
            public bool Monitored { get; set; }
            public int QualityProfileId { get; set; }
            public string Path { get; set; }
            public string RootFolderPath { get; set; }
            public string Tags { get; set; }
            public int? ImportFileId { get; set; }
            public long FileSize { get; set; }
            public string FileQuality { get; set; }
            public string FileLanguages { get; set; }
            public string FileMediaInfo { get; set; }
            public string FileReleaseGroup { get; set; }
            public string FileSceneName { get; set; }
            public string FileOriginalFilePath { get; set; }
            public int FileIndexerFlags { get; set; }
            public string FileEdition { get; set; }
            public DateTime? FileDateAdded { get; set; }
            public bool Resolved { get; set; }
        }

        private class StudioSeedRow
        {
            public string LegacyForeignId { get; set; }
            public string LegacyTpdbId { get; set; }
            public string Title { get; set; }
            public bool Monitored { get; set; }
            public bool MoviesMonitored { get; set; }
            public bool WhisparrMonitorNewItems { get; set; }
            public string RootFolderPath { get; set; }
            public int QualityProfileId { get; set; }
            public string Tags { get; set; }
            public bool Resolved { get; set; }
        }

        private class PerformerSeedRow
        {
            public string LegacyForeignId { get; set; }
            public string LegacyTpdbId { get; set; }
            public string Name { get; set; }
            public bool Monitored { get; set; }
            public bool MoviesMonitored { get; set; }
            public string Tags { get; set; }
        }
    }
}
