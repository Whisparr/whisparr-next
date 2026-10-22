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
            // The movie source stays as it is. Studios and performers carry StashDB ids too, so a library
            // with any of movies, studios or performers counts. An empty library skips this so a fresh
            // install sees the picker.
            InsertWhenLibraryHasContent("whisparrscenemetadatasource", "stashdb");
            InsertWhenLibraryHasContent("metadatasourceslocked", "true");
        }

        private void InsertWhenLibraryHasContent(string key, string value)
        {
            Execute.Sql($@"INSERT INTO ""Config"" (""Key"", ""Value"")
                SELECT '{key}', '{value}'
                WHERE (EXISTS (SELECT 1 FROM ""Movies"")
                    OR EXISTS (SELECT 1 FROM ""Studios"")
                    OR EXISTS (SELECT 1 FROM ""Performers""))
                AND NOT EXISTS (SELECT 1 FROM ""Config"" WHERE ""Key"" = '{key}')");
        }
    }
}
