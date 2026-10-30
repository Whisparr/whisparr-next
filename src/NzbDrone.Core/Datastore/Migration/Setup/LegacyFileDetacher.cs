using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    internal class LegacyRoot
    {
        public int Id { get; set; }
        public string Path { get; set; }
    }

    // Turns a movie row's file into an unmatched ImportFiles row, for the setup steps that take a library row apart.
    // Runs on the new v5 database. Nothing on disk is touched.
    internal static class LegacyFileDetacher
    {
        internal static List<LegacyRoot> LoadRoots(IDbConnection connection, IDbTransaction transaction)
        {
            return connection.Query<LegacyRoot>("SELECT \"Id\", \"Path\" FROM \"RootFolders\"", transaction: transaction).ToList();
        }

        // Turns the movie's file into an ImportFiles row and drops the MovieFiles row. Returns the new ImportFiles id, or
        // null when the file row does not exist.
        internal static int? Detach(IDbConnection connection, IDbTransaction transaction, LegacyRoot root, int movieId, string moviePath, int movieFileId)
        {
            var file = connection.QuerySingleOrDefault<FileRow>(
                "SELECT \"Id\", \"RelativePath\", \"OsHash\", \"PHash\", \"PHashAttemptedAt\", \"HashedFileModifiedAt\" FROM \"MovieFiles\" WHERE \"Id\" = @id",
                new { id = movieFileId },
                transaction);

            if (file == null)
            {
                return null;
            }

            var relativeToMovie = file.RelativePath ?? string.Empty;
            var absolute = Join(moviePath, relativeToMovie);
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
            connection.Execute("UPDATE \"Movies\" SET \"MovieFileId\" = 0 WHERE \"Id\" = @id", new { id = movieId }, transaction);

            return importFileId;
        }

        // The root whose path is the longest prefix of the given path, on a path-segment boundary.
        internal static LegacyRoot FindRoot(List<LegacyRoot> roots, string path)
        {
            return roots
                .Where(r => IsUnder(TrimEnd(r.Path), path))
                .OrderByDescending(r => r.Path.Length)
                .FirstOrDefault();
        }

        internal static bool IsUnder(string root, string path)
        {
            return path.StartsWith(root, StringComparison.Ordinal) &&
                   (path.Length == root.Length || path[root.Length] is '/' or '\\');
        }

        internal static string TrimEnd(string path)
        {
            return path.TrimEnd('/', '\\');
        }

        internal static string Join(string folder, string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return folder;
            }

            var separator = folder.Contains('\\') && !folder.Contains('/') ? '\\' : '/';

            return TrimEnd(folder) + separator + relative.TrimStart('/', '\\');
        }

        internal static string FileNameWithoutExtension(string path)
        {
            var name = path[(path.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
            var dot = name.LastIndexOf('.');

            return dot > 0 ? name[..dot] : name;
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
