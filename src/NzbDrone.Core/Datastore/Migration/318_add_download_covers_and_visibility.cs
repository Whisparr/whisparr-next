using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(318)]
    public class add_download_covers_and_visibility : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Movies").AddColumn("DownloadCovers").AsBoolean().WithDefaultValue(true);
            Alter.Table("Studios").AddColumn("DownloadCovers").AsBoolean().WithDefaultValue(true);
            Alter.Table("Performers").AddColumn("DownloadCovers").AsBoolean().WithDefaultValue(true);
            Alter.Table("Movies").AddColumn("Visibility").AsInt32().WithDefaultValue(1);
            Alter.Table("Studios").AddColumn("Visibility").AsInt32().WithDefaultValue(1);
        }
    }
}
