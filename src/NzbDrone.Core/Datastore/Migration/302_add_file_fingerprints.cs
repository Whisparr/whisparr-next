using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(302)]
    public class add_file_fingerprints : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieFiles").AddColumn("OsHash").AsString().Nullable().Indexed("IX_MovieFiles_OsHash");
            Alter.Table("MovieFiles").AddColumn("PHash").AsString().Nullable();
            Alter.Table("MovieFiles").AddColumn("PHashAttemptedAt").AsDateTime().Nullable();
            Alter.Table("MovieFiles").AddColumn("HashedFileModifiedAt").AsDateTime().Nullable();
            Alter.Table("MovieFiles").AddColumn("MatchTier").AsInt32().NotNullable().WithDefaultValue(0);

            Alter.Table("ImportFiles").AddColumn("OsHash").AsString().Nullable();
            Alter.Table("ImportFiles").AddColumn("PHash").AsString().Nullable();
            Alter.Table("ImportFiles").AddColumn("PHashAttemptedAt").AsDateTime().Nullable();
            Alter.Table("ImportFiles").AddColumn("HashedFileModifiedAt").AsDateTime().Nullable();
        }
    }
}
