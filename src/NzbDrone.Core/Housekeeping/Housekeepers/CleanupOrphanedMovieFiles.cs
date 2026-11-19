using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Rows for a movie that no longer exists, carried over from older databases; they stop file-hash runs.
    // Radarr's version keys on Movies.MovieFileId, which would also delete unmapped rows (MovieId 0) and a
    // scene's extra files, so this one only removes rows whose MovieId names a missing movie.
    public class CleanupOrphanedMovieFiles : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedMovieFiles(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""MovieFiles""
                             WHERE ""Id"" IN (
                             SELECT ""MovieFiles"".""Id"" FROM ""MovieFiles""
                             LEFT OUTER JOIN ""Movies""
                             ON ""MovieFiles"".""MovieId"" = ""Movies"".""Id""
                             WHERE ""MovieFiles"".""MovieId"" > 0
                             AND ""Movies"".""Id"" IS NULL)");
        }
    }
}
