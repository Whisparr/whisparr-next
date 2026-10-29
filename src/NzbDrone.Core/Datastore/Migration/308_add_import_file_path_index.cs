using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(308)]
    public class add_import_file_path_index : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The library import looks files up by path one at a time
            Create.Index().OnTable("ImportFiles").OnColumn("Path").Ascending();
        }
    }
}
