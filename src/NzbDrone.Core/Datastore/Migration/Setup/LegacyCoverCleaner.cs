using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Dapper;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public class LegacyCoverCleanup
    {
        public string ArchivePath { get; set; }
        public List<(string Source, string Destination)> Moves { get; set; } = new();
    }

    public interface ILegacyCoverCleaner
    {
        // Finds the orphaned covers and reports them, without touching them
        LegacyCoverCleanup Prepare(IDbConnection v5);
        void Archive(LegacyCoverCleanup cleanup);
    }

    // Covers live in folders named after the row's autonumber id, and the database build removes rows with raw SQL, so no
    // delete event clears their folders. A later row given the same id would inherit the old images. Rows that survive the
    // build keep their id, so their covers are still right and stay. The orphans are moved aside rather than deleted, so a
    // downgrade can have them back. Raw SQL on the connection it is handed, because in setup mode the container's main
    // database is a dummy.
    public class LegacyCoverCleaner : ILegacyCoverCleaner
    {
        private static readonly Dictionary<string, string> TablesByFolder = new()
        {
            { "movie", "Movies" },
            { "performer", "Performers" },
            { "studio", "Studios" }
        };

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public LegacyCoverCleaner(IAppFolderInfo appFolderInfo)
        {
            _appFolderInfo = appFolderInfo;
            _logger = NzbDroneLogger.GetLogger(this);
        }

        public LegacyCoverCleanup Prepare(IDbConnection v5)
        {
            var coverRoot = _appFolderInfo.GetMediaCoverPath();
            var stamp = DateTime.UtcNow.ToString("yyyy.MM.dd_HH.mm.ss");
            var cleanup = new LegacyCoverCleanup
            {
                ArchivePath = Path.Combine(_appFolderInfo.AppDataFolder, "Backups", "pre-v5", "MediaCover-" + stamp)
            };

            foreach (var (folder, table) in TablesByFolder)
            {
                var path = Path.Combine(coverRoot, folder);

                if (!Directory.Exists(path))
                {
                    continue;
                }

                var ids = v5.Query<int>($"SELECT \"Id\" FROM \"{table}\"").ToHashSet();

                foreach (var coverFolder in Directory.GetDirectories(path))
                {
                    var name = Path.GetFileName(coverFolder);

                    if (int.TryParse(name, out var id) && !ids.Contains(id))
                    {
                        cleanup.Moves.Add((coverFolder, Path.Combine(cleanup.ArchivePath, folder, name)));
                    }
                }
            }

            if (cleanup.Moves.Count > 0)
            {
                Report(v5, cleanup);
            }

            return cleanup;
        }

        // Covers are only a cache, so a folder that can't be moved is no reason to fail a migration that is already in place
        public void Archive(LegacyCoverCleanup cleanup)
        {
            var moved = 0;

            foreach (var (source, destination) in cleanup.Moves)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    Directory.Move(source, destination);
                    moved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Warn(ex, "Could not move the orphaned covers in {0}, they may show on a new item with the same id", source);
                }
            }

            if (moved > 0)
            {
                _logger.Info("Moved {0} orphaned cover folders to {1}", moved, cleanup.ArchivePath);
            }
        }

        private static void Report(IDbConnection v5, LegacyCoverCleanup cleanup)
        {
            var size = cleanup.Moves.Sum(m => new DirectoryInfo(m.Source).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length));

            v5.Execute(
                "INSERT INTO \"LegacyMigrationReportItems\" (\"Category\", \"Area\", \"Subject\", \"Detail\", \"Count\") VALUES (@Category, @Area, @Subject, @Detail, @Count)",
                new
                {
                    Category = (int)LegacyReportCategory.LeftBehind,
                    Area = "MediaCover",
                    Subject = cleanup.ArchivePath,
                    Detail = $"Old covers moved aside, {size.SizeSuffix()}. Delete the folder once you no longer need to go back.",
                    Count = cleanup.Moves.Count
                });
        }
    }
}
