using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(311)]
    public class add_legacy_report_movie_id : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The legacy path can be out of date, so a report row about a scene's file also remembers the scene
            Alter.Table("LegacyMigrationReportItems").AddColumn("MovieId").AsInt32().Nullable();
        }
    }
}
