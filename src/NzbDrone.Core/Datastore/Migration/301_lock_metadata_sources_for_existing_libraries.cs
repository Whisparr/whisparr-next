using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(301)]
    public class lock_metadata_sources_for_existing_libraries : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // An existing library was built from StashDB scenes, so keep that source and lock the choice.
            // The movie source stays as it is. An empty library skips this so a fresh install sees the picker.
            InsertWhenLibraryHasMovies("whisparrscenemetadatasource", "stashdb");
            InsertWhenLibraryHasMovies("metadatasourceslocked", "true");
        }

        private void InsertWhenLibraryHasMovies(string key, string value)
        {
            Execute.Sql($@"INSERT INTO ""Config"" (""Key"", ""Value"")
                SELECT '{key}', '{value}'
                WHERE EXISTS (SELECT 1 FROM ""Movies"")
                AND NOT EXISTS (SELECT 1 FROM ""Config"" WHERE ""Key"" = '{key}')");
        }
    }
}
