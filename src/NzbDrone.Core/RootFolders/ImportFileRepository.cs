using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.RootFolders
{
    public interface IImportFileRepository : IBasicRepository<ImportFile>
    {
        List<ImportFile> FindByRootFolderId(int rootFolderId);
        List<ImportFile> GetFilesWithoutOsHash(int limit);
    }

    public class ImportFileRepository : BasicRepository<ImportFile>, IImportFileRepository
    {
        public ImportFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        protected override bool PublishModelEvents => true;

        public List<ImportFile> FindByRootFolderId(int rootFolderId)
        {
            return Query(x => x.RootFolderId == rootFolderId);
        }

        public List<ImportFile> GetFilesWithoutOsHash(int limit)
        {
            // ORDER BY is the last clause of the select template, so the limit rides along with it
            return Query(Builder()
                .Where<ImportFile>(x => x.OsHash == null)
                .OrderBy($"\"ImportFiles\".\"Id\" ASC LIMIT {limit}"));
        }
    }
}
