using FluentMigrator;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(314)]
    public class add_jav : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Studios")
                .AddColumn("JavMonitored").AsBoolean().WithDefaultValue(false)
                .AddColumn("HasJav").AsBoolean().WithDefaultValue(false);

            Alter.Table("Performers")
                .AddColumn("JavMonitored").AsBoolean().WithDefaultValue(false)
                .AddColumn("HasJav").AsBoolean().WithDefaultValue(false);

            Alter.Table("NamingConfig")
                .AddColumn("RenameJav").AsBoolean().WithDefaultValue(true)
                .AddColumn("StandardJavFormat").AsString().Nullable()
                .AddColumn("JavFolderFormat").AsString().Nullable();

            Update.Table("NamingConfig").Set(new
            {
                StandardJavFormat = "{Scene Code} - {Scene CleanTitle} {[Quality Title]}",
                JavFolderFormat = OsInfo.IsWindows ? "jav\\{Studio CleanTitle}\\{Scene Code}" : "jav/{Studio CleanTitle}/{Scene Code}"
            }).AllRows();

            Create.Index("IX_MovieMetadata_Code").OnTable("MovieMetadata").OnColumn("Code").Ascending();
        }
    }
}
