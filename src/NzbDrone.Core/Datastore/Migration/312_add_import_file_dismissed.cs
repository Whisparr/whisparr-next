using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(312)]
    public class add_import_file_dismissed : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Deleting a dismissed row would bring the file back on the next scan
            Alter.Table("ImportFiles").AddColumn("Dismissed").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
