using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(313)]
    public class add_import_file_top_match : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // AsDateTimeOffset is timestamptz on PostgreSQL, which 302-304 had to be corrected to in 305
            Alter.Table("ImportFiles").AddColumn("TopMatch").AsString().Nullable();
            Alter.Table("ImportFiles").AddColumn("TopMatchCheckedAt").AsDateTimeOffset().Nullable();
        }
    }
}
