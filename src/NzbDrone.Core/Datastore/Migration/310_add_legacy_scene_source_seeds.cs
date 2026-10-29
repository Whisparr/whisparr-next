using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(310)]
    public class add_legacy_scene_source_seeds : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Changing an Eros library's scene source rebuilds it: the studios, performers and scenes wait in these tables
            // with what the user had set, until each is matched on the new source and added.
            Alter.Table("LegacyMigrationState").AddColumn("Rebuild").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.TableForModel("LegacyStudioSeeds")
                  .WithColumn("LegacyForeignId").AsString().NotNullable()
                  .WithColumn("LegacyTpdbId").AsString().Nullable()
                  .WithColumn("Title").AsString().Nullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("MoviesMonitored").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("WhisparrMonitorNewItems").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("AfterDate").AsDateTimeOffset().Nullable()
                  .WithColumn("RootFolderPath").AsString().Nullable()
                  .WithColumn("QualityProfileId").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("Tags").AsString().Nullable()
                  .WithColumn("NewForeignId").AsString().Nullable()
                  .WithColumn("Resolved").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("LastAttemptAt").AsDateTimeOffset().Nullable();

            Create.Index("IX_LegacyStudioSeeds_LegacyForeignId").OnTable("LegacyStudioSeeds").OnColumn("LegacyForeignId");

            Create.TableForModel("LegacyPerformerSeeds")
                  .WithColumn("LegacyForeignId").AsString().NotNullable()
                  .WithColumn("LegacyTpdbId").AsString().Nullable()
                  .WithColumn("Name").AsString().Nullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("MoviesMonitored").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("WhisparrMonitorNewItems").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("AfterDate").AsDateTimeOffset().Nullable()
                  .WithColumn("RootFolderPath").AsString().Nullable()
                  .WithColumn("QualityProfileId").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("Tags").AsString().Nullable()
                  .WithColumn("NewForeignId").AsString().Nullable()
                  .WithColumn("Resolved").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("LastAttemptAt").AsDateTimeOffset().Nullable();

            Create.Index("IX_LegacyPerformerSeeds_LegacyForeignId").OnTable("LegacyPerformerSeeds").OnColumn("LegacyForeignId");

            Create.TableForModel("LegacySceneSeeds")
                  .WithColumn("LegacyForeignId").AsString().NotNullable()
                  .WithColumn("Title").AsString().Nullable()
                  .WithColumn("ReleaseDate").AsDateTimeOffset().Nullable()
                  .WithColumn("LegacyStudioForeignId").AsString().Nullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("QualityProfileId").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("Path").AsString().Nullable()
                  .WithColumn("RootFolderPath").AsString().Nullable()
                  .WithColumn("Tags").AsString().Nullable()
                  .WithColumn("ImportFileId").AsInt32().Nullable()
                  .WithColumn("FileSize").AsInt64().NotNullable().WithDefaultValue(0)
                  .WithColumn("FileQuality").AsString().Nullable()
                  .WithColumn("FileLanguages").AsString().Nullable()
                  .WithColumn("FileMediaInfo").AsString().Nullable()
                  .WithColumn("FileReleaseGroup").AsString().Nullable()
                  .WithColumn("FileSceneName").AsString().Nullable()
                  .WithColumn("FileOriginalFilePath").AsString().Nullable()
                  .WithColumn("FileIndexerFlags").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("FileEdition").AsString().Nullable()
                  .WithColumn("FileDateAdded").AsDateTimeOffset().Nullable()
                  .WithColumn("Resolved").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("LastAttemptAt").AsDateTimeOffset().Nullable();
        }
    }
}
