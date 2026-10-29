using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(306)]
    public class add_legacy_v2_staging : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The v2 library is read into these tables once, then ported from them. Done marks the rows already ported,
            // so an interrupted run resumes where it stopped.
            Create.TableForModel("LegacyV2Series")
                  .WithColumn("LegacyId").AsInt32().NotNullable()
                  .WithColumn("TpdbSiteId").AsInt32().NotNullable()
                  .WithColumn("Title").AsString().NotNullable()
                  .WithColumn("Path").AsString().NotNullable()
                  .WithColumn("RootFolderPath").AsString().Nullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable()
                  .WithColumn("MonitorNewItems").AsInt32().NotNullable()
                  .WithColumn("QualityProfileId").AsInt32().NotNullable()
                  .WithColumn("Tags").AsString().NotNullable()
                  .WithColumn("SeriesType").AsInt32().NotNullable()
                  .WithColumn("Added").AsDateTime().Nullable()
                  .WithColumn("Done").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Index("IX_LegacyV2Series_LegacyId").OnTable("LegacyV2Series").OnColumn("LegacyId");

            Create.TableForModel("LegacyV2Episodes")
                  .WithColumn("LegacyId").AsInt32().NotNullable()
                  .WithColumn("LegacySeriesId").AsInt32().NotNullable()
                  .WithColumn("TpdbSceneId").AsInt32().NotNullable()
                  .WithColumn("ExternalId").AsString().Nullable()
                  .WithColumn("Title").AsString().Nullable()

                  // As v2 stores it
                  .WithColumn("AirDate").AsString().Nullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable()
                  .WithColumn("LegacyEpisodeFileId").AsInt32().NotNullable()
                  .WithColumn("Done").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Index("IX_LegacyV2Episodes_LegacySeriesId").OnTable("LegacyV2Episodes").OnColumn("LegacySeriesId");
            Create.Index("IX_LegacyV2Episodes_LegacyEpisodeFileId").OnTable("LegacyV2Episodes").OnColumn("LegacyEpisodeFileId");

            Create.TableForModel("LegacyV2EpisodeFiles")
                  .WithColumn("LegacyId").AsInt32().NotNullable()
                  .WithColumn("LegacySeriesId").AsInt32().NotNullable()
                  .WithColumn("RelativePath").AsString().NotNullable()
                  .WithColumn("Path").AsString().NotNullable()
                  .WithColumn("Size").AsInt64().NotNullable()
                  .WithColumn("Quality").AsString().NotNullable()
                  .WithColumn("Languages").AsString().NotNullable()
                  .WithColumn("ReleaseGroup").AsString().Nullable()
                  .WithColumn("SceneName").AsString().Nullable()
                  .WithColumn("OriginalFilePath").AsString().Nullable()
                  .WithColumn("MediaInfo").AsString().Nullable()
                  .WithColumn("IndexerFlags").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("DateAdded").AsDateTime().NotNullable()
                  .WithColumn("Done").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Index("IX_LegacyV2EpisodeFiles_LegacyId").OnTable("LegacyV2EpisodeFiles").OnColumn("LegacyId");

            Create.TableForModel("LegacyMigrationReportItems")
                  .WithColumn("Category").AsInt32().NotNullable()
                  .WithColumn("Area").AsString().NotNullable()
                  .WithColumn("Subject").AsString().Nullable()
                  .WithColumn("Detail").AsString().Nullable()
                  .WithColumn("Count").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
