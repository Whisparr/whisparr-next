using System;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // A connection that sees every committed row of a SQLite database without changing the original files.
    // An immutable read-only open ignores an uncheckpointed -wal file (the app was killed uncleanly), so when
    // one exists the db and its WAL files are copied aside and the copy is opened normally, which replays the WAL.
    public sealed class LegacySqliteSnapshot : IDisposable
    {
        private readonly string _tempDir;

        public IDbConnection Connection { get; }

        private LegacySqliteSnapshot(IDbConnection connection, string tempDir)
        {
            Connection = connection;
            _tempDir = tempDir;
        }

        public static LegacySqliteSnapshot Open(string path, string tempDir)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Database file not found", path);
            }

            if (!File.Exists(path + "-wal"))
            {
                return new LegacySqliteSnapshot(LegacyConnections.OpenSqliteReadOnly(path), null);
            }

            // A fresh subfolder every time, so a stale -wal or -shm left in tempDir can never be replayed.
            tempDir = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var copy = Path.Combine(tempDir, Path.GetFileName(path));

                foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                {
                    if (File.Exists(path + suffix))
                    {
                        File.Copy(path + suffix, copy + suffix, true);
                    }
                }

                var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
                {
                    DataSource = copy,
                    FailIfMissing = true,
                    Pooling = false
                }.ConnectionString);

                try
                {
                    connection.Open();
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }

                return new LegacySqliteSnapshot(connection, tempDir);
            }
            catch
            {
                DeleteDir(tempDir);
                throw;
            }
        }

        public void Dispose()
        {
            Connection.Dispose();

            if (_tempDir != null)
            {
                DeleteDir(_tempDir);
            }
        }

        private static void DeleteDir(string dir)
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a leftover temp copy is harmless.
            }
        }
    }
}
