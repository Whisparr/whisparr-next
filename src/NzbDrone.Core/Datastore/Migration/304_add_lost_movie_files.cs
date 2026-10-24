using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(304)]
    public class add_lost_movie_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("LostMovieFiles")
                  .WithColumn("MovieId").AsInt32().NotNullable().Indexed("IX_LostMovieFiles_MovieId")
                  .WithColumn("OsHash").AsString().NotNullable().Indexed("IX_LostMovieFiles_OsHash")
                  .WithColumn("Size").AsInt64().NotNullable()
                  .WithColumn("RelativePath").AsString().NotNullable()
                  .WithColumn("LostAt").AsDateTime().NotNullable();
        }
    }
}
