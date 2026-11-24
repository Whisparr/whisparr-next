using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Not 315: develop builds from #72 recorded the Trakt removal as 315 before #92 moved it to 248,
    // so those installs would skip a new 315 and never get these columns.
    [Migration(316)]
    public class add_tpdb_numeric_id : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieMetadata").AddColumn("TpdbNumericId").AsInt32().Nullable();
            Alter.Table("Studios").AddColumn("TpdbNumericId").AsInt32().Nullable();
            Alter.Table("Performers").AddColumn("TpdbNumericId").AsInt32().Nullable();
        }
    }
}
