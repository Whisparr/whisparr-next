using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.RootFolders
{
    public interface IImportFileRepository : IBasicRepository<ImportFile>
    {
        List<ImportFile> FindByRootFolderId(int rootFolderId);
        ImportFile FindByPath(string path);
        List<ImportFile> GetFilesWithoutOsHash(int afterId, int limit);
        List<ImportFile> GetFilesNeedingPHash(int afterId, int limit);
        void SaveOsHashes(IList<ImportFile> importFiles);
        void SavePHashes(IList<ImportFile> importFiles);
        List<ImportFile> GetFilesNeedingCandidates(int afterId, int limit);
        void SaveCandidates(IList<ImportFile> importFiles);
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

        public ImportFile FindByPath(string path)
        {
            return Query(x => x.Path == path).FirstOrDefault();
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

        public void SaveCandidates(IList<ImportFile> importFiles)
        {
            SetFieldsWithoutEvents(importFiles, f => f.Candidates, f => f.CandidatesCheckedAt);
        }

        public List<ImportFile> GetFilesNeedingCandidates(int afterId, int limit)
        {
            // Rows left unstamped by TryLater are stepped past by the caller's Id watermark
            return Query(Builder()
                .Where<ImportFile>(x => x.OsHash != null && x.CandidatesCheckedAt == null && x.Id > afterId)
                .OrderBy($"\"ImportFiles\".\"Id\" ASC LIMIT {limit}"));
        }

        public List<ImportFile> GetFilesNeedingPHash(int afterId, int limit)
        {
            // Rows skipped without an attempted stamp (missing files) are stepped past by the caller's Id watermark
            return Query(Builder()
                .Where<ImportFile>(x => x.PHash == null && x.PHashAttemptedAt == null && x.Id > afterId)
                .OrderBy($"\"ImportFiles\".\"Id\" ASC LIMIT {limit}"));
        }
    }
}
