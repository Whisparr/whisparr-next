using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(307)]
    public class add_legacy_import_retries : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The library import retries rows that failed, so it counts the attempts and remembers the last one.
            Alter.Table("LegacyMigrationState").AddColumn("ReportDismissed").AsBoolean().NotNullable().WithDefaultValue(false);

            Alter.Table("LegacyV2Series").AddColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0);
            Alter.Table("LegacyV2Series").AddColumn("LastAttemptAt").AsDateTimeOffset().Nullable();

            Alter.Table("LegacyMovieSeeds").AddColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0);
            Alter.Table("LegacyMovieSeeds").AddColumn("LastAttemptAt").AsDateTimeOffset().Nullable();
        }
    }
}
