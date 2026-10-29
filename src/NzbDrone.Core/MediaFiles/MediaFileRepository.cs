using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileRepository : IBasicRepository<MovieFile>
    {
        List<MovieFile> GetFilesByMovie(int movieId);
        List<MovieFile> GetFilesByMovies(IEnumerable<int> movieIds);
        List<MovieFile> GetFilesWithoutMediaInfo();
        List<MovieFile> GetFilesWithoutOsHash(int afterId, int limit);
        List<MovieFile> GetFilesNeedingPHash(int afterId, int limit);
        void SaveOsHashes(IList<MovieFile> movieFiles);
        void SavePHashes(IList<MovieFile> movieFiles);
        void SaveMatchTiers(IList<MovieFile> movieFiles);
        List<MovieFile> GetAllFiles();
        List<MovieFile> GetUnmappedFiles();
        void DeleteForMovies(List<int> movieIds);
        List<MovieFile> GetFilesWithBasePath(string path);
        List<MovieFile> GetFilesWithRelativePath(int movieId, string relativePath);
        MovieFile FindByFullPath(string path);
    }

    public class MediaFileRepository : BasicRepository<MovieFile>, IMediaFileRepository
    {
        public MediaFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<MovieFile> GetFilesByMovie(int movieId)
        {
            return Query(x => x.MovieId == movieId);
        }

        public List<MovieFile> GetFilesByMovies(IEnumerable<int> movieIds)
        {
            return Query(x => movieIds.Contains(x.MovieId));
        }

        public List<MovieFile> GetFilesWithoutMediaInfo()
        {
            return Query(x => x.MediaInfo == null);
        }

        public List<MovieFile> GetFilesWithoutOsHash(int afterId, int limit)
        {
            // ORDER BY is the last clause of the select template, so the limit rides along with it
            return Query(Builder()
                .Where<MovieFile>(x => x.OsHash == null && x.Id > afterId)
                .OrderBy($"\"MovieFiles\".\"Id\" ASC LIMIT {limit}"));
        }

        public List<MovieFile> GetFilesNeedingPHash(int afterId, int limit)
        {
            // Rows skipped without an attempted stamp (missing files) are stepped past by the caller's Id watermark
            return Query(Builder()
                .Where<MovieFile>(x => x.PHash == null && x.PHashAttemptedAt == null && x.Id > afterId)
                .OrderBy($"\"MovieFiles\".\"Id\" ASC LIMIT {limit}"));
        }

        public void SaveOsHashes(IList<MovieFile> movieFiles)
        {
            SetFieldsWithoutEvents(movieFiles, f => f.OsHash, f => f.HashedFileModifiedAt);
        }

        public void SavePHashes(IList<MovieFile> movieFiles)
        {
            SetFieldsWithoutEvents(movieFiles, f => f.PHash, f => f.PHashAttemptedAt);
        }

        public void SaveMatchTiers(IList<MovieFile> movieFiles)
        {
            SetFieldsWithoutEvents(movieFiles, f => f.MatchTier);
        }

        public List<MovieFile> GetAllFiles()
        {
            return Query(x => x.Size > 0);
        }

        public List<MovieFile> GetUnmappedFiles()
        {
            return Query(x => x.MovieId == 0);
        }

        public void DeleteForMovies(List<int> movieIds)
        {
            Delete(x => movieIds.Contains(x.MovieId));
        }

        public List<MovieFile> GetFilesWithBasePath(string path)
        {
            // ensure path ends with a single trailing path separator to avoid matching partial paths
            var safePath = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return _database.Query<MovieFile>(new SqlBuilder(_database.DatabaseType).Where<MovieFile>(x => x.OriginalFilePath.StartsWith(safePath))).ToList();
        }

        public List<MovieFile> GetFilesWithRelativePath(int movieId, string relativePath)
        {
            return Query(c => c.MovieId == movieId && c.RelativePath == relativePath);
        }

        // MovieFile.Path isn't stored, so a file is tracked when some movie folder plus the row's RelativePath
        // makes the path. Only the path's ancestors can be that folder, so the join narrows on Movies.Path.
        // Returns a light row (Id, MovieId, RelativePath, Path), not a hydrated MovieFile.
        public MovieFile FindByFullPath(string path)
        {
            var folders = new List<string>();
            var folder = path.GetParentPath();

            while (folder.IsNotNullOrWhiteSpace())
            {
                folders.Add(folder);
                folder = folder.GetParentPath();
            }

            if (folders.Count == 0)
            {
                return null;
            }

            // One (folder, relative path) pair per ancestor, as indexed parameters: list expansion differs between SQLite and Postgres
            var parameters = new DynamicParameters();
            var pairs = new List<string>();

            for (var i = 0; i < folders.Count; i++)
            {
                parameters.Add($"F{i}", folders[i]);
                parameters.Add($"R{i}", folders[i].GetRelativePath(path));
                pairs.Add($"(\"Movies\".\"Path\" = @F{i} AND \"MovieFiles\".\"RelativePath\" = @R{i})");
            }

            using (var conn = _database.OpenConnection())
            {
                var rows = conn.Query<(int Id, int MovieId, string RelativePath, string Folder)>(
                    "SELECT \"MovieFiles\".\"Id\", \"MovieFiles\".\"MovieId\", \"MovieFiles\".\"RelativePath\", \"Movies\".\"Path\" AS \"Folder\" " +
                    "FROM \"MovieFiles\" JOIN \"Movies\" ON \"Movies\".\"Id\" = \"MovieFiles\".\"MovieId\" " +
                    "WHERE " + string.Join(" OR ", pairs),
                    parameters);

                var match = rows.FirstOrDefault(r => r.Folder.IsNotNullOrWhiteSpace() &&
                                                     System.IO.Path.Combine(r.Folder, r.RelativePath).PathEquals(path));

                if (match.Id == 0)
                {
                    return null;
                }

                return new MovieFile
                {
                    Id = match.Id,
                    MovieId = match.MovieId,
                    RelativePath = match.RelativePath,
                    Path = path
                };
            }
        }
    }
}
