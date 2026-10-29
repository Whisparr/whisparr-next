using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Npgsql;
using NzbDrone.Common.Disk;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    // Copies the v2 library (Series, Episodes, EpisodeFiles) into the v5 staging tables, so the later import never
    // touches the legacy database, and counts the legacy tables that are not carried over.
    public class V2LibrarySnapshot
    {
        private const int PageSize = 1000;
        private const string LibraryArea = "Library";

        private static readonly string[] LeftBehindTables =
        {
            "ImportLists",
            "ImportListExclusions",
            "CustomFilters",
            "History",
            "DownloadHistory",
            "Blocklist",
            "PendingReleases",
            "Commands",
            "ScheduledTasks",
            "UpdateHistory",
            "ExtraFiles",
            "SubtitleFiles",
            "MetadataFiles"
        };

        public V2SnapshotCounts Snapshot(V2PortContext context)
        {
            var counts = new V2SnapshotCounts();

            var roots = context.Legacy
                .Query<string>("SELECT \"Path\" FROM \"RootFolders\"")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => (Path: p, Os: new OsPath(p)))
                .ToList();

            var seriesPaths = new Dictionary<int, string>();

            counts.Series = SnapshotSeries(context, roots, seriesPaths);
            counts.Episodes = SnapshotEpisodes(context);
            counts.EpisodeFiles = SnapshotEpisodeFiles(context, seriesPaths);

            return counts;
        }

        public void CountLeftBehind(V2PortContext context)
        {
            var existing = ListTables(context).ToHashSet(StringComparer.Ordinal);

            var tables = LeftBehindTables
                .Concat(existing.Where(t => t.EndsWith("Status", StringComparison.Ordinal)).OrderBy(t => t, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal);

            foreach (var table in tables.Where(existing.Contains))
            {
                var count = context.Legacy.ExecuteScalar<long>($"SELECT COUNT(*) FROM {Quote(table)}");

                if (count > 0)
                {
                    context.Add(LegacyReportCategory.LeftBehind, table, table, "Not carried over to v5", (int)count);
                }
            }
        }

        // ---- Series ----

        private static int SnapshotSeries(V2PortContext context, List<(string Path, OsPath Os)> roots, Dictionary<int, string> seriesPaths)
        {
            var total = 0;

            foreach (var page in Pages(context, "Series", "Id, TvdbId, Title, Path, Monitored, MonitorNewItems, QualityProfileId, Tags, SeriesType, Added"))
            {
                var batch = new List<object>();

                foreach (var row in page)
                {
                    var legacyId = Convert.ToInt32(row["Id"]);
                    var title = V2QualityPorter.Text(row, "Title");
                    var path = V2QualityPorter.Text(row, "Path") ?? string.Empty;
                    var root = FindRoot(roots, path);

                    seriesPaths[legacyId] = path;

                    if (root == null)
                    {
                        context.Add(LegacyReportCategory.NeedsAttention, LibraryArea, title, path);
                    }

                    batch.Add(new
                    {
                        LegacyId = legacyId,
                        TpdbSiteId = Convert.ToInt32(row["TvdbId"]),
                        Title = title ?? string.Empty,
                        Path = path,
                        RootFolderPath = root,
                        Monitored = V2QualityPorter.Bool(row, "Monitored"),
                        MonitorNewItems = Convert.ToInt32(row["MonitorNewItems"]),
                        QualityProfileId = Convert.ToInt32(row["QualityProfileId"]),
                        Tags = V2QualityPorter.Text(row, "Tags") ?? "[]",
                        SeriesType = Convert.ToInt32(row["SeriesType"]),
                        Added = ToDate(row, "Added")
                    });
                }

                Insert(
                    context,
                    "LegacyV2Series",
                    "LegacyId, TpdbSiteId, Title, Path, RootFolderPath, Monitored, MonitorNewItems, QualityProfileId, Tags, SeriesType, Added",
                    batch);

                total += batch.Count;
            }

            return total;
        }

        // The longest root folder that contains the series path, compared on whole path segments.
        private static string FindRoot(List<(string Path, OsPath Os)> roots, string seriesPath)
        {
            var series = new OsPath(seriesPath);

            return roots
                .Where(r => r.Os.Contains(series))
                .OrderByDescending(r => r.Path.TrimEnd('/', '\\').Length)
                .Select(r => r.Path)
                .FirstOrDefault();
        }

        // ---- Episodes ----

        private static int SnapshotEpisodes(V2PortContext context)
        {
            var total = 0;

            foreach (var page in Pages(context, "Episodes", "Id, SeriesId, TvdbId, ExternalId, Title, AirDate, Monitored, EpisodeFileId"))
            {
                var batch = page
                    .Select(row => (object)new
                    {
                        LegacyId = Convert.ToInt32(row["Id"]),
                        LegacySeriesId = Convert.ToInt32(row["SeriesId"]),
                        TpdbSceneId = ToInt(row, "TvdbId"),
                        ExternalId = V2QualityPorter.Text(row, "ExternalId"),
                        Title = V2QualityPorter.Text(row, "Title"),
                        AirDate = V2QualityPorter.Text(row, "AirDate"),
                        Monitored = V2QualityPorter.Bool(row, "Monitored"),
                        LegacyEpisodeFileId = ToInt(row, "EpisodeFileId")
                    })
                    .ToList();

                Insert(
                    context,
                    "LegacyV2Episodes",
                    "LegacyId, LegacySeriesId, TpdbSceneId, ExternalId, Title, AirDate, Monitored, LegacyEpisodeFileId",
                    batch);

                total += batch.Count;
            }

            return total;
        }

        // ---- Episode files ----

        private static int SnapshotEpisodeFiles(V2PortContext context, Dictionary<int, string> seriesPaths)
        {
            var total = 0;

            foreach (var page in Pages(context, "EpisodeFiles", "Id, SeriesId, RelativePath, Size, Quality, Languages, ReleaseGroup, SceneName, OriginalFilePath, MediaInfo, IndexerFlags, DateAdded"))
            {
                var batch = new List<object>();

                foreach (var row in page)
                {
                    var seriesId = Convert.ToInt32(row["SeriesId"]);
                    var relativePath = V2QualityPorter.Text(row, "RelativePath") ?? string.Empty;

                    seriesPaths.TryGetValue(seriesId, out var seriesPath);

                    batch.Add(new
                    {
                        LegacyId = Convert.ToInt32(row["Id"]),
                        LegacySeriesId = seriesId,
                        RelativePath = relativePath,
                        Path = JoinPath(seriesPath, relativePath),
                        Size = Convert.ToInt64(row["Size"]),
                        Quality = RemapQuality(V2QualityPorter.Text(row, "Quality")),
                        Languages = RemapLanguages(V2QualityPorter.Text(row, "Languages")),
                        ReleaseGroup = V2QualityPorter.Text(row, "ReleaseGroup"),
                        SceneName = V2QualityPorter.Text(row, "SceneName"),
                        OriginalFilePath = V2QualityPorter.Text(row, "OriginalFilePath"),
                        MediaInfo = V2QualityPorter.Text(row, "MediaInfo"),
                        IndexerFlags = V2QualityMap.IndexerFlags(ToInt(row, "IndexerFlags")),
                        DateAdded = ToDate(row, "DateAdded") ?? DateTime.UtcNow
                    });
                }

                Insert(
                    context,
                    "LegacyV2EpisodeFiles",
                    "LegacyId, LegacySeriesId, RelativePath, Path, Size, Quality, Languages, ReleaseGroup, SceneName, OriginalFilePath, MediaInfo, IndexerFlags, DateAdded",
                    batch);

                total += batch.Count;
            }

            return total;
        }

        private static string JoinPath(string seriesPath, string relativePath)
        {
            if (string.IsNullOrEmpty(seriesPath))
            {
                return relativePath;
            }

            try
            {
                return (new OsPath(seriesPath) + new OsPath(relativePath)).FullPath;
            }
            catch (Exception)
            {
                // Mixed path styles that OsPath refuses to combine
                return seriesPath.TrimEnd('/', '\\') + "/" + relativePath.TrimStart('/', '\\');
            }
        }

        // {"quality": 7, "revision": {...}}; quality may also be an object carrying the id.
        private static string RemapQuality(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json ?? "{}";
            }

            try
            {
                if (JsonNode.Parse(json) is JsonObject root)
                {
                    if (root["quality"] is JsonObject quality)
                    {
                        var id = ReadInt(quality["id"]);

                        if (id.HasValue)
                        {
                            quality["id"] = V2QualityMap.QualityId(id.Value);
                        }
                    }
                    else
                    {
                        var id = ReadInt(root["quality"]);

                        if (id.HasValue)
                        {
                            root["quality"] = V2QualityMap.QualityId(id.Value);
                        }
                    }

                    return root.ToJsonString();
                }
            }
            catch (JsonException)
            {
                // Not JSON: keep what v2 stored
            }

            return json;
        }

        // A JSON array of ids or of objects carrying one; languages v5 doesn't have are dropped.
        private static string RemapLanguages(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "[]";
            }

            try
            {
                if (JsonNode.Parse(json) is JsonArray languages)
                {
                    var result = new JsonArray();

                    foreach (var language in languages)
                    {
                        if (language is JsonObject item)
                        {
                            var id = ReadInt(item["id"]);
                            var mapped = id.HasValue ? V2QualityMap.Language(id.Value) : null;

                            if (mapped.HasValue)
                            {
                                item["id"] = mapped.Value;
                                result.Add(JsonNode.Parse(item.ToJsonString()));
                            }
                        }
                        else
                        {
                            var id = ReadInt(language);
                            var mapped = id.HasValue ? V2QualityMap.Language(id.Value) : null;

                            if (mapped.HasValue)
                            {
                                result.Add(mapped.Value);
                            }
                        }
                    }

                    return result.ToJsonString();
                }
            }
            catch (JsonException)
            {
                // Not JSON: keep what v2 stored
            }

            return json;
        }

        // ---- Helpers ----

        // Reads a table in pages of PageSize by Id, so a library of hundreds of thousands of rows never sits in memory.
        private static IEnumerable<List<IDictionary<string, object>>> Pages(V2PortContext context, string table, string columns)
        {
            var last = 0;
            var columnList = string.Join(", ", columns.Split(',').Select(c => Quote(c.Trim())));

            while (true)
            {
                var page = context.Legacy
                    .Query($"SELECT {columnList} FROM {Quote(table)} WHERE \"Id\" > @last ORDER BY \"Id\" LIMIT {PageSize}", new { last })
                    .Cast<IDictionary<string, object>>()
                    .ToList();

                if (page.Count == 0)
                {
                    yield break;
                }

                yield return page;

                last = Convert.ToInt32(page[^1]["Id"]);
            }
        }

        // The staging tables take their own Id, so only the named columns are written.
        private static void Insert(V2PortContext context, string table, string columns, List<object> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            var names = columns.Split(',').Select(c => c.Trim()).ToList();
            var sql = $"INSERT INTO {Quote(table)} ({string.Join(", ", names.Select(Quote))}) VALUES ({string.Join(", ", names.Select(n => "@" + n))})";

            context.V5.Execute(sql, rows, context.Transaction);
        }

        private static IEnumerable<string> ListTables(V2PortContext context)
        {
            // PostgreSQL legacy sources are Npgsql connections; anything else is SQLite.
            var sql = context.Legacy is NpgsqlConnection
                ? "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'"
                : "SELECT name FROM sqlite_master WHERE type = 'table'";

            return context.Legacy.Query<string>(sql).ToList();
        }

        private static int ToInt(IDictionary<string, object> row, string column)
        {
            return row.TryGetValue(column, out var value) && value != null && value is not DBNull ? Convert.ToInt32(value) : 0;
        }

        // PostgreSQL gives UTC DateTimes, which Npgsql refuses to write to a timestamp column, so the kind is dropped.
        private static DateTime? ToDate(IDictionary<string, object> row, string column)
        {
            if (!row.TryGetValue(column, out var value) || value == null || value is DBNull)
            {
                return null;
            }

            var date = value is DateTime dt
                ? dt
                : DateTime.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

            // v2 stores UTC, and the staging columns are timestamptz on PostgreSQL, which refuses an unspecified kind.
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }

        private static int? ReadInt(JsonNode node)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out var i))
                {
                    return i;
                }

                if (value.TryGetValue<double>(out var d) && d == Math.Floor(d))
                {
                    return (int)d;
                }
            }

            return null;
        }

        private static string Quote(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
