using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Removing a root folder leaves its unmapped files behind
    public class CleanupOrphanedImportFiles : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedImportFiles(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""ImportFiles""
                             WHERE ""Id"" IN (
                             SELECT ""ImportFiles"".""Id"" FROM ""ImportFiles""
                             LEFT OUTER JOIN ""RootFolders""
                             ON ""ImportFiles"".""RootFolderId"" = ""RootFolders"".""Id""
                             WHERE ""RootFolders"".""Id"" IS NULL)");
        }
    }
}
