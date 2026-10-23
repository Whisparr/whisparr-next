using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.RootFolders
{
    public interface IImportFileRepository : IBasicRepository<ImportFile>
    {
        List<ImportFile> FindByRootFolderId(int rootFolderId);
        List<ImportFile> GetFilesWithoutOsHash(int afterId, int limit);
        List<ImportFile> GetFilesNeedingPHash(int limit);
        void SaveOsHashes(IList<ImportFile> importFiles);
        void SavePHashes(IList<ImportFile> importFiles);
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

        public List<ImportFile> GetFilesWithoutOsHash(int afterId, int limit)
        {
            // ORDER BY is the last clause of the select template, so the limit rides along with it
            return Query(Builder()
                .Where<ImportFile>(x => x.OsHash == null && x.Id > afterId)
                .OrderBy($"\"ImportFiles\".\"Id\" ASC LIMIT {limit}"));
        }

        public void SaveOsHashes(IList<ImportFile> importFiles)
        {
            SetFieldsWithoutEvents(importFiles, f => f.OsHash, f => f.HashedFileModifiedAt);
        }

        public void SavePHashes(IList<ImportFile> importFiles)
        {
            SetFieldsWithoutEvents(importFiles, f => f.PHash, f => f.PHashAttemptedAt);
        }

        public List<ImportFile> GetFilesNeedingPHash(int limit)
        {
            // Every processed row gets PHashAttemptedAt, so it leaves this query and no watermark is needed
            return Query(Builder()
                .Where<ImportFile>(x => x.PHash == null && x.PHashAttemptedAt == null)
                .OrderBy($"\"ImportFiles\".\"Id\" ASC LIMIT {limit}"));
        }
    }
}
