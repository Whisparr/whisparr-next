using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(305)]
    public class add_legacy_migration_state : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // 302-304 declared these with AsDateTime(), which is timestamp without time zone on PostgreSQL. SQLite
            // stores both as DATETIME, and its alter-column table rebuild would drop column defaults and the
            // MovieFiles json_extract index, so only PostgreSQL is altered.
            var postgres = IfDatabase(ProcessorIdConstants.PostgreSQL);

            postgres.Alter.Table("MovieFiles").AlterColumn("PHashAttemptedAt").AsDateTimeOffset().Nullable();
            postgres.Alter.Table("MovieFiles").AlterColumn("HashedFileModifiedAt").AsDateTimeOffset().Nullable();
            postgres.Alter.Table("ImportFiles").AlterColumn("PHashAttemptedAt").AsDateTimeOffset().Nullable();
            postgres.Alter.Table("ImportFiles").AlterColumn("HashedFileModifiedAt").AsDateTimeOffset().Nullable();
            postgres.Alter.Table("ImportFiles").AlterColumn("CandidatesCheckedAt").AsDateTimeOffset().Nullable();
            postgres.Alter.Table("LostMovieFiles").AlterColumn("LostAt").AsDateTimeOffset().NotNullable();

            Create.TableForModel("LegacyMigrationState")
                  .WithColumn("SourceKind").AsInt32().NotNullable()
                  .WithColumn("SceneSource").AsInt32().NotNullable()
                  .WithColumn("MovieSource").AsInt32().NotNullable()
                  .WithColumn("Phase").AsString().NotNullable()
                  .WithColumn("Progress").AsString().Nullable()
                  .WithColumn("Completed").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("StartedAt").AsDateTimeOffset().Nullable()
                  .WithColumn("CompletedAt").AsDateTimeOffset().Nullable()
                  .WithColumn("BackupPath").AsString().Nullable();

            Create.TableForModel("LegacyMovieSeeds")
                  .WithColumn("LegacyForeignId").AsString().NotNullable()
                  .WithColumn("Title").AsString().NotNullable()
                  .WithColumn("Year").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("TmdbId").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("ImdbId").AsString().Nullable()
                  .WithColumn("Monitored").AsBoolean().NotNullable()
                  .WithColumn("QualityProfileId").AsInt32().NotNullable()
                  .WithColumn("RootFolderPath").AsString().Nullable()
                  .WithColumn("Tags").AsString().Nullable()
                  .WithColumn("ImportFileId").AsInt32().Nullable()
                  .WithColumn("Resolved").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
