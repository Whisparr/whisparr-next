using NzbDrone.Common.Exceptions;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore
{
    public class LegacyDatabaseException : WhisparrStartupException
    {
        public LegacyDatabaseException(string databaseName, MigrationType migrationType)
            : base(BuildMessage(databaseName, migrationType))
        {
        }

        private static string BuildMessage(string databaseName, MigrationType migrationType)
        {
            var isMain = migrationType == MigrationType.Main;
            var kind = isMain ? "main" : "log";
            var setting = isMain ? "MainDb" : "LogDb";

            return $"{databaseName} was created by Whisparr v2 or v3 and can't be used by v5, which keeps its own {kind} database. " +
                   $"If Whisparr__Postgres__{setting} (or Postgres{setting} in config5.xml) still names a legacy database, point it at a new, empty one. " +
                   "Legacy installs are migrated by the v5 setup wizard.";
        }
    }
}
