using System;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.LostFiles;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupExpiredLostMovieFiles : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupExpiredLostMovieFiles(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""LostMovieFiles"" WHERE ""LostAt"" < @Cutoff",
                new { Cutoff = DateTime.UtcNow - LostMovieFileService.Lifetime });
        }
    }
}
