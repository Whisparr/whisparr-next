using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(303)]
    public class add_import_file_candidates : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportFiles").AddColumn("Candidates").AsString().Nullable();
            Alter.Table("ImportFiles").AddColumn("CandidatesCheckedAt").AsDateTime().Nullable();
        }
    }
}
