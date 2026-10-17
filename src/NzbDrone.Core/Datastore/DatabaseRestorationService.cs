using System;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore
{
    public interface IRestoreDatabase
    {
        void Restore();
    }

    public class DatabaseRestorationService : IRestoreDatabase
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DatabaseRestorationService));

        public DatabaseRestorationService(IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
        {
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
        }

        public void Restore()
        {
            var dbRestorePath = _appFolderInfo.GetDatabaseRestore();

            if (!_diskProvider.FileExists(dbRestorePath))
            {
                return;
            }

            // A legacy or unreadable file would replace the v5 database and only be refused after it was gone
            if (!LegacyDatabaseGuard.IsRestorableSqliteFile(dbRestorePath))
            {
                var rejectedPath = dbRestorePath + ".rejected";

                Logger.Error("Not restoring {0}: it is a Whisparr v2/v3 database or not a SQLite database. The current database is unchanged; the file was moved to {1}", dbRestorePath, rejectedPath);
                _diskProvider.MoveFile(dbRestorePath, rejectedPath, true);

                return;
            }

            try
            {
                Logger.Info("Restoring Database");

                var dbPath = _appFolderInfo.GetDatabase();

                _diskProvider.DeleteFile(dbPath + "-shm");
                _diskProvider.DeleteFile(dbPath + "-wal");
                _diskProvider.DeleteFile(dbPath + "-journal");
                _diskProvider.DeleteFile(dbPath);

                _diskProvider.MoveFile(dbRestorePath, dbPath);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to restore database");
                throw;
            }
        }
    }
}
