using System;
using System.Data;
using Dapper;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public interface IV5DatabaseFinisher
    {
        void Finish(IDbConnection v5, IDbTransaction tx, SceneMetadataType scene, MovieMetadataType movie, LegacyMigrationState state);
    }

    // Locks the chosen metadata sources, disables file and folder renaming and records the migration state. Raw SQL on the connection it is
    // handed, because in setup mode the container's main database is a dummy.
    public class V5DatabaseFinisher : IV5DatabaseFinisher
    {
        public void Finish(IDbConnection v5, IDbTransaction tx, SceneMetadataType scene, MovieMetadataType movie, LegacyMigrationState state)
        {
            // Values as ConfigService writes them: enums lowercased, bools as bool.ToString()
            UpsertConfig(v5, tx, "whisparrscenemetadatasource", scene.ToString().ToLower());
            UpsertConfig(v5, tx, "whisparrmoviemetadatasource", movie.ToString().ToLower());
            UpsertConfig(v5, tx, "metadatasourceslocked", true.ToString());

            // Eros copies its Config wholesale, and a refresh of a movie with a file would rename its folder mid-migration
            UpsertConfig(v5, tx, "autorenamefolders", false.ToString());

            v5.Execute(
                "UPDATE \"NamingConfig\" SET \"RenameScenes\" = @rename, \"RenameMovies\" = @rename",
                new { rename = false },
                tx);

            v5.Execute(
                "INSERT INTO \"LegacyMigrationState\" (\"SourceKind\", \"SceneSource\", \"MovieSource\", \"Phase\", \"Progress\", \"Completed\", \"StartedAt\", \"CompletedAt\", \"BackupPath\") " +
                "VALUES (@SourceKind, @SceneSource, @MovieSource, @Phase, @Progress, @Completed, @StartedAt, @CompletedAt, @BackupPath)",
                new
                {
                    SourceKind = (int)state.SourceKind,
                    SceneSource = (int)state.SceneSource,
                    MovieSource = (int)state.MovieSource,
                    state.Phase,
                    state.Progress,
                    state.Completed,
                    StartedAt = ToUtc(state.StartedAt),
                    CompletedAt = ToUtc(state.CompletedAt),
                    state.BackupPath
                },
                tx);
        }

        // The app's Dapper handler writes DateTime as UTC, and the columns read back through it
        private static DateTime? ToUtc(DateTime? value)
        {
            return value.HasValue ? DateTime.SpecifyKind(value.Value.ToUniversalTime(), DateTimeKind.Utc) : null;
        }

        private static void UpsertConfig(IDbConnection v5, IDbTransaction tx, string key, string value)
        {
            var updated = v5.Execute("UPDATE \"Config\" SET \"Value\" = @value WHERE \"Key\" = @key", new { key, value }, tx);

            if (updated == 0)
            {
                v5.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES (@key, @value)", new { key, value }, tx);
            }
        }
    }
}
