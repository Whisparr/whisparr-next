using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Nodes;
using Dapper;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    // Indexers, download clients, notifications and metadata consumers.
    // Settings JSON is edited as nodes: only the keys v5 renamed or dropped change, unknown keys stay because v5 ignores them.
    public class V2ProviderPorter
    {
        private const string IndexersArea = "Indexers";
        private const string DownloadClientsArea = "DownloadClients";
        private const string NotificationsArea = "Notifications";
        private const string MetadataArea = "Metadata";

        private static readonly HashSet<string> SkippedDownloadClients = new (StringComparer.Ordinal) { "RQBit", "Tribler" };
        private static readonly HashSet<string> SkippedIndexers = new (StringComparer.Ordinal) { "Fanzub" };
        private static readonly HashSet<string> MappedMetadata = new (StringComparer.Ordinal) { "XbmcMetadata", "RoksboxMetadata", "WdtvMetadata" };

        private static readonly string[] NewznabV2OnlyKeys = { "seriesNameSource", "searchSiteTitleOnly", "searchTitleOnly", "searchExternalIdOnly", "dateSearchFormat" };

        private static readonly (string From, string To)[] DownloadClientRenames =
        {
            ("tvCategory", "movieCategory"),
            ("tvImportedCategory", "movieImportedCategory"),
            ("tvDirectory", "movieDirectory"),
            ("recentTvPriority", "recentMoviePriority"),
            ("olderTvPriority", "olderMoviePriority")
        };

        public void Port(V2PortContext context)
        {
            var skippedClients = PortDownloadClients(context);

            PortIndexers(context, skippedClients);
            PortNotifications(context);
            PortMetadata(context);
        }

        // ---- Download clients ----

        private static HashSet<int> PortDownloadClients(V2PortContext context)
        {
            var skipped = new HashSet<int>();

            context.V5.Execute("DELETE FROM \"DownloadClients\"", transaction: context.Transaction);

            foreach (var row in V2QualityPorter.ReadRows(context, "DownloadClients"))
            {
                var id = Convert.ToInt32(row["Id"]);
                var name = V2QualityPorter.Text(row, "Name");
                var implementation = V2QualityPorter.Text(row, "Implementation");

                if (SkippedDownloadClients.Contains(implementation))
                {
                    skipped.Add(id);
                    context.Add(LegacyReportCategory.SkippedSetting, DownloadClientsArea, name, $"{implementation} download clients do not exist in v5");
                    continue;
                }

                var settings = ParseSettings(V2QualityPorter.Text(row, "Settings"));

                foreach (var (from, to) in DownloadClientRenames)
                {
                    Rename(settings, from, to);
                }

                if (implementation == "Flood" && TryTake(settings, "startOnAdd", out var startOnAdd) && startOnAdd is JsonValue value && value.TryGetValue<bool>(out var start))
                {
                    Set(settings, "addPaused", !start);
                }

                V2QualityPorter.Insert(context, "DownloadClients", true, new Dictionary<string, object>
                {
                    ["Id"] = id,
                    ["Enable"] = V2QualityPorter.Bool(row, "Enable"),
                    ["Name"] = name,
                    ["Implementation"] = implementation,
                    ["Settings"] = settings.ToJsonString(),
                    ["ConfigContract"] = V2QualityPorter.Text(row, "ConfigContract"),
                    ["Priority"] = Convert.ToInt32(row["Priority"]),
                    ["RemoveCompletedDownloads"] = V2QualityPorter.Bool(row, "RemoveCompletedDownloads"),
                    ["RemoveFailedDownloads"] = V2QualityPorter.Bool(row, "RemoveFailedDownloads"),
                    ["Tags"] = V2QualityPorter.Text(row, "Tags")
                });
            }

            context.InsertedTables.Add("DownloadClients");

            return skipped;
        }

        // ---- Indexers ----

        private static void PortIndexers(V2PortContext context, HashSet<int> skippedClients)
        {
            context.V5.Execute("DELETE FROM \"Indexers\"", transaction: context.Transaction);

            foreach (var row in V2QualityPorter.ReadRows(context, "Indexers"))
            {
                var name = V2QualityPorter.Text(row, "Name");
                var implementation = V2QualityPorter.Text(row, "Implementation");

                if (SkippedIndexers.Contains(implementation))
                {
                    context.Add(LegacyReportCategory.SkippedSetting, IndexersArea, name, $"{implementation} indexers do not exist in v5");
                    continue;
                }

                var settings = ParseSettings(V2QualityPorter.Text(row, "Settings"));

                if (implementation is "Newznab" or "Torznab")
                {
                    foreach (var key in NewznabV2OnlyKeys)
                    {
                        TryTake(settings, key, out _);
                    }
                }

                var downloadClientId = Convert.ToInt32(row["DownloadClientId"]);

                if (downloadClientId != 0 && skippedClients.Contains(downloadClientId))
                {
                    downloadClientId = 0;
                    context.Add(LegacyReportCategory.NeedsAttention, IndexersArea, name, "Its download client was not ported, so the indexer now uses any download client");
                }

                V2QualityPorter.Insert(context, "Indexers", true, new Dictionary<string, object>
                {
                    ["Id"] = Convert.ToInt32(row["Id"]),
                    ["EnableRss"] = NullableBool(row, "EnableRss"),
                    ["EnableAutomaticSearch"] = V2QualityPorter.Bool(row, "EnableAutomaticSearch"),
                    ["EnableInteractiveSearch"] = V2QualityPorter.Bool(row, "EnableInteractiveSearch"),
                    ["Name"] = name,
                    ["Implementation"] = implementation,
                    ["Settings"] = settings.ToJsonString(),
                    ["ConfigContract"] = V2QualityPorter.Text(row, "ConfigContract"),
                    ["Priority"] = Convert.ToInt32(row["Priority"]),
                    ["Tags"] = V2QualityPorter.Text(row, "Tags"),
                    ["DownloadClientId"] = downloadClientId
                });
            }

            context.InsertedTables.Add("Indexers");
        }

        // ---- Notifications ----

        private static void PortNotifications(V2PortContext context)
        {
            context.V5.Execute("DELETE FROM \"Notifications\"", transaction: context.Transaction);

            foreach (var row in V2QualityPorter.ReadRows(context, "Notifications"))
            {
                var name = V2QualityPorter.Text(row, "Name");
                var implementation = V2QualityPorter.Text(row, "Implementation");
                var settings = ParseSettings(V2QualityPorter.Text(row, "Settings"));

                switch (implementation)
                {
                    case "Email":
                        if (TryTake(settings, "requireEncryption", out var require))
                        {
                            // Always = 1, Preferred = 0.
                            var always = require is JsonValue value && value.TryGetValue<bool>(out var b) && b;
                            Set(settings, "useEncryption", always ? 1 : 0);
                        }

                        break;
                    case "Gotify":
                        Rename(settings, "includeSeriesPoster", "includeMoviePoster");
                        break;
                    case "Telegram":
                        TryTake(settings, "linkPreview", out _);
                        break;
                }

                if (V2QualityPorter.Bool(row, "OnImportComplete"))
                {
                    context.Add(LegacyReportCategory.SkippedSetting, NotificationsArea, name, "The On Import Complete event has no v5 equivalent");
                }

                V2QualityPorter.Insert(context, "Notifications", true, new Dictionary<string, object>
                {
                    ["Id"] = Convert.ToInt32(row["Id"]),
                    ["Name"] = name,
                    ["OnGrab"] = V2QualityPorter.Bool(row, "OnGrab"),
                    ["OnDownload"] = V2QualityPorter.Bool(row, "OnDownload"),
                    ["Settings"] = settings.ToJsonString(),
                    ["Implementation"] = implementation,
                    ["ConfigContract"] = V2QualityPorter.Text(row, "ConfigContract"),
                    ["OnUpgrade"] = NullableBool(row, "OnUpgrade"),
                    ["Tags"] = V2QualityPorter.Text(row, "Tags"),
                    ["OnRename"] = V2QualityPorter.Bool(row, "OnRename"),
                    ["OnHealthIssue"] = V2QualityPorter.Bool(row, "OnHealthIssue"),
                    ["IncludeHealthWarnings"] = V2QualityPorter.Bool(row, "IncludeHealthWarnings"),
                    ["OnMovieDelete"] = V2QualityPorter.Bool(row, "OnSeriesDelete"),
                    ["OnMovieFileDelete"] = V2QualityPorter.Bool(row, "OnEpisodeFileDelete"),
                    ["OnMovieFileDeleteForUpgrade"] = V2QualityPorter.Bool(row, "OnEpisodeFileDeleteForUpgrade"),
                    ["OnApplicationUpdate"] = V2QualityPorter.Bool(row, "OnApplicationUpdate"),
                    ["OnMovieAdded"] = V2QualityPorter.Bool(row, "OnSeriesAdd"),
                    ["OnHealthRestored"] = V2QualityPorter.Bool(row, "OnHealthRestored"),
                    ["OnManualInteractionRequired"] = V2QualityPorter.Bool(row, "OnManualInteractionRequired")
                });
            }

            context.InsertedTables.Add("Notifications");
        }

        // ---- Metadata ----

        // v5 also creates missing default definitions at startup, so only the mapped v2 rows go in.
        private static void PortMetadata(V2PortContext context)
        {
            context.V5.Execute("DELETE FROM \"Metadata\"", transaction: context.Transaction);

            foreach (var row in V2QualityPorter.ReadRows(context, "Metadata"))
            {
                var name = V2QualityPorter.Text(row, "Name");
                var implementation = V2QualityPorter.Text(row, "Implementation");
                var enabled = V2QualityPorter.Bool(row, "Enable");

                if (!MappedMetadata.Contains(implementation))
                {
                    if (enabled)
                    {
                        context.Add(LegacyReportCategory.SkippedSetting, MetadataArea, name, $"{implementation} does not exist in v5");
                    }

                    continue;
                }

                var v2 = ParseSettings(V2QualityPorter.Text(row, "Settings"));

                // Every other v5 setting takes its default.
                var settings = new JsonObject();

                CopyAs(v2, "episodeMetadata", settings, "movieMetadata");
                CopyAs(v2, "episodeImages", settings, "movieImages");

                if (implementation == "XbmcMetadata")
                {
                    CopyAs(v2, "seriesMetadataUrl", settings, "movieMetadataURL");
                }

                V2QualityPorter.Insert(context, "Metadata", true, new Dictionary<string, object>
                {
                    ["Id"] = Convert.ToInt32(row["Id"]),
                    ["Enable"] = enabled,
                    ["Name"] = name,
                    ["Implementation"] = implementation,
                    ["Settings"] = settings.ToJsonString(),
                    ["ConfigContract"] = V2QualityPorter.Text(row, "ConfigContract")
                });
            }

            context.InsertedTables.Add("Metadata");
        }

        // ---- Helpers ----

        private static JsonObject ParseSettings(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }

        private static string FindKey(JsonObject obj, string key)
        {
            return obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryTake(JsonObject obj, string key, out JsonNode value)
        {
            var actual = FindKey(obj, key);

            if (actual == null)
            {
                value = null;
                return false;
            }

            obj.Remove(actual, out value);

            return true;
        }

        private static void Set(JsonObject obj, string key, JsonNode value)
        {
            obj[FindKey(obj, key) ?? key] = value;
        }

        private static void Rename(JsonObject obj, string from, string to)
        {
            if (TryTake(obj, from, out var value))
            {
                Set(obj, to, value);
            }
        }

        private static void CopyAs(JsonObject source, string from, JsonObject target, string to)
        {
            var key = FindKey(source, from);

            if (key != null && source[key] != null)
            {
                target[to] = source[key].DeepClone();
            }
        }

        // Nullable v2 booleans stay null.
        private static object NullableBool(IDictionary<string, object> row, string column)
        {
            return row.TryGetValue(column, out var value) && value != null && value is not DBNull ? Convert.ToBoolean(value) : null;
        }
    }
}
