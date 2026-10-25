using System;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Text;
using Npgsql;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public interface IPreV5BackupService
    {
        // Returns the zip path. Throws SetupStepException on any failure, after deleting a partial zip.
        string Backup(LegacyInstall install, string legacyConfigPath);
    }

    public class PreV5BackupService : IPreV5BackupService
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly PostgresTableCopier _tableCopier;

        public PreV5BackupService(IAppFolderInfo appFolderInfo, PostgresTableCopier tableCopier)
        {
            _appFolderInfo = appFolderInfo;
            _tableCopier = tableCopier;
        }

        public string Backup(LegacyInstall install, string legacyConfigPath)
        {
            var backupDir = Path.Combine(_appFolderInfo.AppDataFolder, "Backups", "pre-v5");
            var stamp = DateTime.UtcNow.ToString("yyyy.MM.dd_HH.mm.ss");
            var zipPath = Path.Combine(backupDir, stamp + ".zip");
            var staging = Path.Combine(backupDir, "staging-" + stamp);

            try
            {
                Directory.CreateDirectory(staging);

                if (install.Database.Type == DatabaseType.PostgreSQL)
                {
                    BackupPostgres(install.Database, staging);
                }
                else
                {
                    BackupSqlite(install.Database, staging);
                }

                if (!string.IsNullOrEmpty(legacyConfigPath) && File.Exists(legacyConfigPath))
                {
                    File.Copy(legacyConfigPath, Path.Combine(staging, "config.xml"), true);
                }

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                ZipFile.CreateFromDirectory(staging, zipPath);

                return zipPath;
            }
            catch (Exception ex)
            {
                DeleteFile(zipPath);

                throw new SetupStepException("The backup of your existing install could not be created. Nothing has been changed. " + ex.Message, ex);
            }
            finally
            {
                DeleteDirectory(staging);
            }
        }

        private static void BackupSqlite(LegacyDatabaseLocation location, string staging)
        {
            var name = Path.GetFileName(location.SqlitePath);
            var target = Path.Combine(staging, name);

            using (var snapshot = LegacySqliteSnapshot.Open(location.SqlitePath, Path.Combine(staging, "snapshot")))
            {
                var backupConnectionString = new SQLiteConnectionStringBuilder
                {
                    DataSource = target,
                    JournalMode = SQLiteJournalModeEnum.Truncate,
                    Pooling = false
                }.ConnectionString;

                using var backupConnection = new SQLiteConnection(backupConnectionString);
                backupConnection.Open();

                ((SQLiteConnection)snapshot.Connection).BackupDatabase(backupConnection, "main", "main", -1, null, 500);

                // The backup changes the journal_mode, force it to truncate again.
                using var command = backupConnection.CreateCommand();
                command.CommandText = "pragma journal_mode=truncate";
                command.ExecuteNonQuery();
            }

            // Truncate mode leaves an empty journal behind once the backup connection closes.
            DeleteFile(target + "-journal");
        }

        private void BackupPostgres(LegacyDatabaseLocation location, string staging)
        {
            using var connection = (NpgsqlConnection)LegacyConnections.OpenPostgres(location.PostgresConnectionString);

            File.WriteAllText(Path.Combine(staging, "schema.sql"), _tableCopier.DescribeSchema(connection), new UTF8Encoding(false));

            var dataDir = Path.Combine(staging, "data");
            Directory.CreateDirectory(dataDir);

            foreach (var table in _tableCopier.ListTables(connection))
            {
                using var stream = File.Create(Path.Combine(dataDir, table + ".copy"));
                _tableCopier.CopyOut(connection, table, stream);
            }
        }

        private static void DeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort cleanup.
            }
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort cleanup.
            }
        }
    }
}
