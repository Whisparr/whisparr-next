using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public class MixedMovieResult
    {
        public int Kept { get; set; }
        public int Merged { get; set; }
        public int Detached { get; set; }

        // Other-source movies outside every root folder: left untouched
        public int Skipped { get; set; }
        public List<string> SkippedPaths { get; } = new();
    }

    public interface IMixedMovieResolver
    {
        // Runs on the new v5 database, never on a legacy one. Movie rows from the source that was not chosen are merged
        // into a twin from the chosen source, or turned into a seed plus an unmatched import file. Scenes are not touched.
        MixedMovieResult Resolve(IDbConnection v5Connection, IDbTransaction transaction, MovieMetadataType chosen);
    }

    public class MixedMovieResolver : IMixedMovieResolver
    {
        private const int ItemTypeMovie = 0;
        private const int SourceTmdb = 0;
        private const int SourceTpdbMovie = 2;

        public MixedMovieResult Resolve(IDbConnection v5Connection, IDbTransaction transaction, MovieMetadataType chosen)
        {
            var chosenSource = chosen switch
            {
                MovieMetadataType.TMDB => SourceTmdb,
                MovieMetadataType.TPDB => SourceTpdbMovie,
                _ => throw new ArgumentException("A movie metadata source must be chosen", nameof(chosen))
            };

            var movies = v5Connection.Query<MovieRow>(
                "SELECT m.\"Id\" AS \"MovieId\", mm.\"Id\" AS \"MetadataId\", mm.\"ForeignId\", mm.\"MetadataSource\", mm.\"TmdbId\", mm.\"ImdbId\", " +
                "mm.\"Title\", mm.\"Year\", m.\"Path\", m.\"Monitored\", m.\"QualityProfileId\", m.\"Tags\", m.\"MovieFileId\" " +
                "FROM \"Movies\" m INNER JOIN \"MovieMetadata\" mm ON mm.\"Id\" = m.\"MovieMetadataId\" " +
                "WHERE mm.\"ItemType\" = @itemType ORDER BY m.\"Id\"",
                new { itemType = ItemTypeMovie },
                transaction).ToList();

            var kept = movies.Where(x => x.MetadataSource == chosenSource).ToList();
            var others = movies.Where(x => x.MetadataSource != chosenSource).ToList();
            var result = new MixedMovieResult { Kept = kept.Count };

            if (others.Count == 0)
            {
                return result;
            }

            var roots = v5Connection.Query<RootRow>("SELECT \"Id\", \"Path\" FROM \"RootFolders\"", transaction: transaction).ToList();

            // Only a real TMDB id links two rows; a title match could merge two different movies silently.
            var keptByTmdb = new Dictionary<int, MovieRow>();

            foreach (var row in kept.Where(x => x.TmdbId.GetValueOrDefault() > 0))
            {
                keptByTmdb.TryAdd(row.TmdbId.Value, row);
            }

            foreach (var other in others)
            {
                var twin = other.TmdbId.GetValueOrDefault() > 0 && keptByTmdb.TryGetValue(other.TmdbId.Value, out var found) ? found : null;
                var root = other.Path == null ? null : FindRoot(roots, other.Path);
                var movesOntoTwin = twin != null && (other.MovieFileId == 0 || twin.MovieFileId == 0);

                // A detached file needs a root folder to hang off, and a seed needs one for its path. Without it the
                // row is left exactly as it is, for the migration report.
                if (other.Path == null || (root == null && !movesOntoTwin))
                {
                    result.Skipped++;
                    result.SkippedPaths.Add(other.Path ?? $"Movie {other.MovieId}");

                    continue;
                }

                ResolveOther(v5Connection, transaction, result, other, twin, root, movesOntoTwin);
                DeleteMovie(v5Connection, transaction, other);
            }

            return result;
        }

        // Merged counts twins whose file moved onto the kept row or that had none; a detached file counts as Detached.
        private static void ResolveOther(IDbConnection connection, IDbTransaction transaction, MixedMovieResult result, MovieRow other, MovieRow twin, RootRow root, bool movesOntoTwin)
        {
            if (movesOntoTwin)
            {
                result.Merged++;

                if (other.MovieFileId > 0)
                {
                    MoveFile(connection, transaction, other, twin);
                }

                return;
            }

            var importFileId = other.MovieFileId > 0 ? Detach(connection, transaction, root, other) : null;

            if (importFileId != null)
            {
                result.Detached++;
            }

            if (twin == null)
            {
                InsertSeed(connection, transaction, root, other, importFileId);
            }
        }

        private static void MoveFile(IDbConnection connection, IDbTransaction transaction, MovieRow from, MovieRow to)
        {
            connection.Execute("UPDATE \"MovieFiles\" SET \"MovieId\" = @movieId WHERE \"Id\" = @fileId", new { movieId = to.MovieId, fileId = from.MovieFileId }, transaction);
            connection.Execute("UPDATE \"Movies\" SET \"MovieFileId\" = @fileId WHERE \"Id\" = @movieId", new { movieId = to.MovieId, fileId = from.MovieFileId }, transaction);

            to.MovieFileId = from.MovieFileId;
            from.MovieFileId = 0;
        }

        // Turns the movie's file into an ImportFiles row and drops the MovieFiles row. Nothing on disk is touched.
        private static int? Detach(IDbConnection connection, IDbTransaction transaction, RootRow root, MovieRow movie)
        {
            var file = connection.QuerySingleOrDefault<FileRow>(
                "SELECT \"Id\", \"RelativePath\", \"OsHash\", \"PHash\", \"PHashAttemptedAt\", \"HashedFileModifiedAt\" FROM \"MovieFiles\" WHERE \"Id\" = @id",
                new { id = movie.MovieFileId },
                transaction);

            if (file == null)
            {
                return null;
            }

            var relativeToMovie = file.RelativePath ?? string.Empty;
            var absolute = Join(movie.Path, relativeToMovie);
            var relativePath = absolute[TrimEnd(root.Path).Length..].TrimStart('/', '\\');

            var importFileId = connection.ExecuteScalar<int>(
                "INSERT INTO \"ImportFiles\" (\"RootFolderId\", \"Path\", \"RelativePath\", \"Name\", \"OsHash\", \"PHash\", \"PHashAttemptedAt\", \"HashedFileModifiedAt\") " +
                "VALUES (@rootFolderId, @path, @relativePath, @name, @osHash, @pHash, @pHashAttemptedAt, @hashedFileModifiedAt) RETURNING \"Id\"",
                new
                {
                    rootFolderId = root.Id,
                    path = absolute,
                    relativePath,
                    name = FileNameWithoutExtension(relativeToMovie),
                    osHash = file.OsHash,
                    pHash = file.PHash,
                    pHashAttemptedAt = file.PHashAttemptedAt,
                    hashedFileModifiedAt = file.HashedFileModifiedAt
                },
                transaction);

            connection.Execute("DELETE FROM \"MovieFiles\" WHERE \"Id\" = @id", new { id = file.Id }, transaction);
            connection.Execute("UPDATE \"Movies\" SET \"MovieFileId\" = 0 WHERE \"Id\" = @id", new { id = movie.MovieId }, transaction);

            movie.MovieFileId = 0;

            return importFileId;
        }

        private static void InsertSeed(IDbConnection connection, IDbTransaction transaction, RootRow root, MovieRow movie, int? importFileId)
        {
            connection.Execute(
                "INSERT INTO \"LegacyMovieSeeds\" (\"LegacyForeignId\", \"Title\", \"Year\", \"TmdbId\", \"ImdbId\", \"Monitored\", \"QualityProfileId\", \"RootFolderPath\", \"Tags\", \"ImportFileId\", \"Resolved\") " +
                "VALUES (@foreignId, @title, @year, @tmdbId, @imdbId, @monitored, @qualityProfileId, @rootFolderPath, @tags, @importFileId, @resolved)",
                new
                {
                    foreignId = movie.ForeignId,
                    title = movie.Title,
                    year = movie.Year.GetValueOrDefault(),
                    tmdbId = movie.TmdbId.GetValueOrDefault(),
                    imdbId = movie.ImdbId,
                    monitored = movie.Monitored,
                    qualityProfileId = movie.QualityProfileId,
                    rootFolderPath = root.Path,
                    tags = movie.Tags,
                    importFileId,
                    resolved = false
                },
                transaction);
        }

        private static void DeleteMovie(IDbConnection connection, IDbTransaction transaction, MovieRow movie)
        {
            var args = new { movieId = movie.MovieId, metadataId = movie.MetadataId };

            connection.Execute("DELETE FROM \"Credits\" WHERE \"MovieMetadataId\" = @metadataId", args, transaction);
            connection.Execute("DELETE FROM \"AlternativeTitles\" WHERE \"MovieMetadataId\" = @metadataId", args, transaction);
            connection.Execute("DELETE FROM \"Movies\" WHERE \"Id\" = @movieId", args, transaction);
            connection.Execute("DELETE FROM \"MovieMetadata\" WHERE \"Id\" = @metadataId", args, transaction);
        }

        // The root whose path is the longest prefix of the given path, on a path-segment boundary.
        private static RootRow FindRoot(List<RootRow> roots, string path)
        {
            return roots
                .Where(r => IsUnder(TrimEnd(r.Path), path))
                .OrderByDescending(r => r.Path.Length)
                .FirstOrDefault();
        }

        private static bool IsUnder(string root, string path)
        {
            return path.StartsWith(root, StringComparison.Ordinal) &&
                   (path.Length == root.Length || path[root.Length] is '/' or '\\');
        }

        private static string TrimEnd(string path)
        {
            return path.TrimEnd('/', '\\');
        }

        private static string Join(string folder, string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return folder;
            }

            var separator = folder.Contains('\\') && !folder.Contains('/') ? '\\' : '/';

            return TrimEnd(folder) + separator + relative.TrimStart('/', '\\');
        }

        private static string FileNameWithoutExtension(string path)
        {
            var name = path[(path.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
            var dot = name.LastIndexOf('.');

            return dot > 0 ? name[..dot] : name;
        }

        private class MovieRow
        {
            public int MovieId { get; set; }
            public int MetadataId { get; set; }
            public string ForeignId { get; set; }
            public int MetadataSource { get; set; }
            public int? TmdbId { get; set; }
            public string ImdbId { get; set; }
            public string Title { get; set; }
            public int? Year { get; set; }
            public string Path { get; set; }
            public bool Monitored { get; set; }
            public int QualityProfileId { get; set; }
            public string Tags { get; set; }
            public int MovieFileId { get; set; }
        }

        private class RootRow
        {
            public int Id { get; set; }
            public string Path { get; set; }
        }

        private class FileRow
        {
            public int Id { get; set; }
            public string RelativePath { get; set; }
            public string OsHash { get; set; }
            public string PHash { get; set; }
            public DateTime? PHashAttemptedAt { get; set; }
            public DateTime? HashedFileModifiedAt { get; set; }
        }
    }
}
