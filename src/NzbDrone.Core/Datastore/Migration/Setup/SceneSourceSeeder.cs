using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NzbDrone.Core.ImportLists.ImportExclusions;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public class SceneSeedResult
    {
        public int Studios { get; set; }
        public int Performers { get; set; }
        public int Scenes { get; set; }

        // Scene files that became unmatched import files
        public int Detached { get; set; }

        // Scene files outside every root folder, which were not linked
        public int Dropped { get; set; }

        // Phase 2 matches every seed on the new scene source
        public bool NeedsPhase2 => Studios + Performers + Scenes > 0;
    }

    public interface ISceneSourceSeeder
    {
        // Runs on the new v5 database, never on a legacy one. Turns the studios, performers and scenes of a copied Eros
        // library into seed rows, because their ids belong to the scene source that is being left. Movies are not touched.
        SceneSeedResult Seed(IDbConnection v5Connection, IDbTransaction transaction);
    }

    public class SceneSourceSeeder : ISceneSourceSeeder
    {
        private const int ItemTypeScene = 1;

        // The metadata of every scene, as ItemTypeScene
        private const string SceneItems = "SELECT \"Id\" FROM \"MovieMetadata\" WHERE \"ItemType\" = 1";

        // Tables whose rows for a seeded scene stay where they are, and are only counted for the report
        private static readonly string[] LeftBehindTables = { "ExtraFiles", "SubtitleFiles", "MetadataFiles", "History", "Blocklist" };

        public SceneSeedResult Seed(IDbConnection v5Connection, IDbTransaction transaction)
        {
            var result = new SceneSeedResult();

            SeedScenes(v5Connection, transaction, result);
            result.Studios = SeedStudios(v5Connection, transaction);
            result.Performers = SeedPerformers(v5Connection, transaction);

            RewriteExclusions(v5Connection, transaction);
            DropImportListMovies(v5Connection, transaction);
            CountLeftBehind(v5Connection, transaction);
            DeleteSourceRows(v5Connection, transaction);

            return result;
        }

        private static void SeedScenes(IDbConnection connection, IDbTransaction transaction, SceneSeedResult result)
        {
            var scenes = connection.Query<SceneRow>(
                "SELECT m.\"Id\" AS \"MovieId\", m.\"Path\", m.\"MovieFileId\", f.\"Id\" AS \"FileId\", f.\"RelativePath\" " +
                "FROM \"Movies\" m INNER JOIN \"MovieMetadata\" mm ON mm.\"Id\" = m.\"MovieMetadataId\" " +
                "LEFT JOIN \"MovieFiles\" f ON f.\"Id\" = m.\"MovieFileId\" " +
                "WHERE mm.\"ItemType\" = @itemType ORDER BY m.\"Id\"",
                new { itemType = ItemTypeScene },
                transaction).ToList();

            var roots = LegacyFileDetacher.LoadRoots(connection, transaction);

            foreach (var scene in scenes)
            {
                var root = scene.Path == null ? null : LegacyFileDetacher.FindRoot(roots, scene.Path);
                var hasFile = scene.FileId != null;

                // A file with no root folder to hang off can't become an import file
                var linkable = hasFile && root != null;

                var seedId = InsertSceneSeed(connection, transaction, scene.MovieId, linkable ? scene.FileId.Value : 0, root);

                if (linkable)
                {
                    var importFileId = LegacyFileDetacher.Detach(connection, transaction, root, scene.MovieId, scene.Path, scene.MovieFileId);

                    connection.Execute("UPDATE \"LegacySceneSeeds\" SET \"ImportFileId\" = @importFileId WHERE \"Id\" = @seedId", new { importFileId, seedId }, transaction);

                    result.Detached++;
                }
                else if (hasFile)
                {
                    connection.Execute("DELETE FROM \"MovieFiles\" WHERE \"Id\" = @id", new { id = scene.FileId }, transaction);
                    connection.Execute("UPDATE \"Movies\" SET \"MovieFileId\" = 0 WHERE \"Id\" = @id", new { id = scene.MovieId }, transaction);

                    var subject = scene.Path == null ? $"Movie {scene.MovieId}" : LegacyFileDetacher.Join(scene.Path, scene.RelativePath);

                    AddReport(connection, transaction, LegacyReportCategory.NeedsAttention, "Scenes", subject, "Outside every root folder; not linked", 1);

                    result.Dropped++;
                }

                result.Scenes++;
            }
        }

        // fileId 0 gives a seed without file columns
        private static int InsertSceneSeed(IDbConnection connection, IDbTransaction transaction, int movieId, int fileId, LegacyRoot root)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO \"LegacySceneSeeds\" (\"LegacyForeignId\", \"Title\", \"ReleaseDate\", \"LegacyStudioForeignId\", \"Monitored\", \"QualityProfileId\", \"Path\", \"RootFolderPath\", \"Tags\", " +
                "\"FileSize\", \"FileQuality\", \"FileLanguages\", \"FileMediaInfo\", \"FileReleaseGroup\", \"FileSceneName\", \"FileOriginalFilePath\", \"FileIndexerFlags\", \"FileEdition\", \"FileDateAdded\") " +
                "SELECT mm.\"ForeignId\", mm.\"Title\", mm.\"ReleaseDateUtc\", mm.\"StudioForeignId\", COALESCE(m.\"Monitored\", false), COALESCE(m.\"QualityProfileId\", 0), m.\"Path\", @rootFolderPath, m.\"Tags\", " +
                "COALESCE(f.\"Size\", 0), f.\"Quality\", f.\"Languages\", f.\"MediaInfo\", f.\"ReleaseGroup\", f.\"SceneName\", f.\"OriginalFilePath\", COALESCE(f.\"IndexerFlags\", 0), f.\"Edition\", f.\"DateAdded\" " +
                "FROM \"Movies\" m INNER JOIN \"MovieMetadata\" mm ON mm.\"Id\" = m.\"MovieMetadataId\" " +
                "LEFT JOIN \"MovieFiles\" f ON f.\"Id\" = @fileId " +
                "WHERE m.\"Id\" = @movieId RETURNING \"Id\"",
                new { movieId, fileId, rootFolderPath = root?.Path },
                transaction);
        }

        private static int SeedStudios(IDbConnection connection, IDbTransaction transaction)
        {
            return connection.Execute(
                "INSERT INTO \"LegacyStudioSeeds\" (\"LegacyForeignId\", \"LegacyTpdbId\", \"Title\", \"Monitored\", \"MoviesMonitored\", \"WhisparrMonitorNewItems\", \"AfterDate\", \"RootFolderPath\", \"QualityProfileId\", \"Tags\") " +
                "SELECT \"ForeignId\", \"TpdbId\", \"Title\", COALESCE(\"Monitored\", false), COALESCE(\"MoviesMonitored\", false), COALESCE(\"WhisparrMonitorNewItems\", false), \"AfterDate\", \"RootFolderPath\", COALESCE(\"QualityProfileId\", 0), \"Tags\" " +
                "FROM \"Studios\" ORDER BY \"Id\"",
                transaction: transaction);
        }

        // A hidden performer was only ever a credit's name, so it is not worth matching
        private static int SeedPerformers(IDbConnection connection, IDbTransaction transaction)
        {
            return connection.Execute(
                "INSERT INTO \"LegacyPerformerSeeds\" (\"LegacyForeignId\", \"LegacyTpdbId\", \"Name\", \"Monitored\", \"MoviesMonitored\", \"WhisparrMonitorNewItems\", \"AfterDate\", \"RootFolderPath\", \"QualityProfileId\", \"Tags\") " +
                "SELECT \"ForeignId\", \"TpdbId\", \"Name\", COALESCE(\"Monitored\", false), COALESCE(\"MoviesMonitored\", false), COALESCE(\"WhisparrMonitorNewItems\", false), \"AfterDate\", \"RootFolderPath\", COALESCE(\"QualityProfileId\", 0), \"Tags\" " +
                "FROM \"Performers\" WHERE COALESCE(\"Visibility\", 1) = 1 ORDER BY \"Id\"",
                transaction: transaction);
        }

        // A studio or performer exclusion that knows its TPDB id keeps working on the new source. The rest name ids
        // that no longer exist, and a scene exclusion does too.
        private static void RewriteExclusions(IDbConnection connection, IDbTransaction transaction)
        {
            var tpdbIds = new Dictionary<(ImportExclusionType Type, string ForeignId), string>();

            AddTpdbIds(connection, transaction, tpdbIds, ImportExclusionType.Studio, "Studios");
            AddTpdbIds(connection, transaction, tpdbIds, ImportExclusionType.Performer, "Performers");

            var exclusions = connection.Query<ExclusionRow>("SELECT \"Id\", \"ForeignId\", \"Type\" FROM \"ImportExclusions\"", transaction: transaction).ToList();

            // ForeignId is unique, so a rewrite must not land on an id that is already excluded
            var taken = exclusions.Select(x => x.ForeignId).ToHashSet();
            var deleted = new Dictionary<ImportExclusionType, int>();

            foreach (var exclusion in exclusions)
            {
                if (!Enum.TryParse<ImportExclusionType>(exclusion.Type, true, out var type) ||
                    type is not (ImportExclusionType.Scene or ImportExclusionType.Studio or ImportExclusionType.Performer))
                {
                    continue;
                }

                var known = tpdbIds.TryGetValue((type, exclusion.ForeignId), out var tpdbId);

                if (known && tpdbId == exclusion.ForeignId)
                {
                    continue;
                }

                if (known && taken.Add(tpdbId))
                {
                    connection.Execute("UPDATE \"ImportExclusions\" SET \"ForeignId\" = @tpdbId WHERE \"Id\" = @id", new { tpdbId, id = exclusion.Id }, transaction);
                    taken.Remove(exclusion.ForeignId);

                    continue;
                }

                connection.Execute("DELETE FROM \"ImportExclusions\" WHERE \"Id\" = @id", new { id = exclusion.Id }, transaction);
                deleted[type] = deleted.GetValueOrDefault(type) + 1;
            }

            foreach (var type in new[] { ImportExclusionType.Scene, ImportExclusionType.Studio, ImportExclusionType.Performer })
            {
                AddReport(connection, transaction, LegacyReportCategory.LeftBehind, "ImportExclusions", type.ToString(), "Not carried over to the new scene source", deleted.GetValueOrDefault(type));
            }
        }

        private static void AddTpdbIds(IDbConnection connection, IDbTransaction transaction, Dictionary<(ImportExclusionType Type, string ForeignId), string> tpdbIds, ImportExclusionType type, string table)
        {
            var rows = connection.Query<(string ForeignId, string TpdbId)>(
                $"SELECT \"ForeignId\", \"TpdbId\" FROM \"{table}\" WHERE \"ForeignId\" IS NOT NULL AND \"TpdbId\" IS NOT NULL AND \"TpdbId\" <> ''",
                transaction: transaction);

            foreach (var (foreignId, tpdbId) in rows)
            {
                tpdbIds[(type, foreignId)] = tpdbId;
            }
        }

        private static void DropImportListMovies(IDbConnection connection, IDbTransaction transaction)
        {
            var count = connection.Execute("DELETE FROM \"ImportListMovies\"", transaction: transaction);

            AddReport(connection, transaction, LegacyReportCategory.LeftBehind, "ImportListMovies", "ImportListMovies", "Not carried over to the new scene source", count);
        }

        private static void CountLeftBehind(IDbConnection connection, IDbTransaction transaction)
        {
            foreach (var table in LeftBehindTables)
            {
                var count = connection.ExecuteScalar<int>(
                    $"SELECT COUNT(*) FROM \"{table}\" WHERE \"MovieId\" IN (SELECT m.\"Id\" FROM \"Movies\" m INNER JOIN \"MovieMetadata\" mm ON mm.\"Id\" = m.\"MovieMetadataId\" WHERE mm.\"ItemType\" = @itemType)",
                    new { itemType = ItemTypeScene },
                    transaction);

                AddReport(connection, transaction, LegacyReportCategory.LeftBehind, table, table, "Rows for the scenes were left in place", count);
            }
        }

        // In the order MixedMovieResolver deletes a movie, for every scene at once
        private static void DeleteSourceRows(IDbConnection connection, IDbTransaction transaction)
        {
            connection.Execute($"DELETE FROM \"Credits\" WHERE \"MovieMetadataId\" IN ({SceneItems})", transaction: transaction);
            connection.Execute($"DELETE FROM \"AlternativeTitles\" WHERE \"MovieMetadataId\" IN ({SceneItems})", transaction: transaction);
            connection.Execute($"DELETE FROM \"Movies\" WHERE \"MovieMetadataId\" IN ({SceneItems})", transaction: transaction);
            connection.Execute("DELETE FROM \"MovieMetadata\" WHERE \"ItemType\" = @itemType", new { itemType = ItemTypeScene }, transaction);
            connection.Execute("DELETE FROM \"Studios\"", transaction: transaction);
            connection.Execute("DELETE FROM \"Performers\"", transaction: transaction);
        }

        // A count of 0 is not worth a line in the report
        private static void AddReport(IDbConnection connection, IDbTransaction transaction, LegacyReportCategory category, string area, string subject, string detail, int count)
        {
            if (count == 0)
            {
                return;
            }

            connection.Execute(
                "INSERT INTO \"LegacyMigrationReportItems\" (\"Category\", \"Area\", \"Subject\", \"Detail\", \"Count\") VALUES (@Category, @Area, @Subject, @Detail, @Count)",
                new { Category = (int)category, Area = area, Subject = subject, Detail = detail, Count = count },
                transaction);
        }

        private class SceneRow
        {
            public int MovieId { get; set; }
            public string Path { get; set; }
            public int MovieFileId { get; set; }
            public int? FileId { get; set; }
            public string RelativePath { get; set; }
        }

        private class ExclusionRow
        {
            public int Id { get; set; }
            public string ForeignId { get; set; }
            public string Type { get; set; }
        }
    }
}
