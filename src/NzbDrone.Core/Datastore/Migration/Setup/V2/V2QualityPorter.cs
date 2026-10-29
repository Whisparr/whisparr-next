using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Nodes;
using Dapper;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    // Quality profiles and definitions, custom formats, release profiles and auto tagging.
    // Works on JSON nodes, never through v2 types.
    public class V2QualityPorter
    {
        private const string QualityArea = "Quality";
        private const string CustomFormatsArea = "CustomFormats";
        private const string ReleaseProfilesArea = "ReleaseProfiles";
        private const string AutoTaggingArea = "AutoTagging";

        private static readonly string[] SizeProperties = { "minSize", "maxSize", "preferredSize" };

        public void Port(V2PortContext context)
        {
            var skippedFormats = PortCustomFormats(context);

            PortQualityProfiles(context, skippedFormats);
            PortQualityDefinitions(context);
            PortReleaseProfiles(context);
            PortAutoTagging(context);
        }

        // ---- Quality profiles ----

        private static void PortQualityProfiles(V2PortContext context, HashSet<int> skippedFormats)
        {
            context.V5.Execute("DELETE FROM \"QualityProfileQualityRanks\"", transaction: context.Transaction);
            context.V5.Execute("DELETE FROM \"QualityProfiles\"", transaction: context.Transaction);

            foreach (var row in ReadRows(context, "QualityProfiles"))
            {
                var items = JsonNode.Parse(Text(row, "Items") ?? "[]").AsArray();
                var dropped = RemapItems(items);

                if (dropped > 0)
                {
                    context.Add(LegacyReportCategory.SkippedSetting, QualityArea, Text(row, "Name"), "Qualities with no v5 equivalent were removed from the profile", dropped);
                }

                AddMissingQualities(items);

                var cutoff = Convert.ToInt32(row["Cutoff"]);

                Insert(context, "QualityProfiles", true, new Dictionary<string, object>
                {
                    ["Id"] = Convert.ToInt32(row["Id"]),
                    ["Name"] = Text(row, "Name"),
                    ["Cutoff"] = cutoff < 1000 ? V2QualityMap.QualityId(cutoff) : cutoff,
                    ["Items"] = items.ToJsonString(),
                    ["Language"] = Languages.Language.Any.Id,
                    ["UpgradeAllowed"] = Bool(row, "UpgradeAllowed"),
                    ["Fallback"] = false,
                    ["FormatItems"] = FilterFormatItems(Text(row, "FormatItems"), skippedFormats),
                    ["MinFormatScore"] = Convert.ToInt32(row["MinFormatScore"]),
                    ["CutoffFormatScore"] = Convert.ToInt32(row["CutoffFormatScore"]),
                    ["MinUpgradeFormatScore"] = Convert.ToInt32(row["MinUpgradeFormatScore"])
                });
            }

            context.InsertedTables.Add("QualityProfiles");
        }

        // Remaps every quality at any depth and strips the per-item sizes. Returns how many items were removed because v5 has no such quality.
        private static int RemapItems(JsonArray items)
        {
            var removed = 0;
            var known = Quality.All.Select(q => q.Id).ToHashSet();

            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] is not JsonObject item)
                {
                    continue;
                }

                foreach (var property in SizeProperties)
                {
                    item.Remove(property);
                }

                if (item["items"] is JsonArray children)
                {
                    removed += RemapItems(children);
                }

                var quality = item["quality"];

                // v5 stores a quality as its bare id, so an object form is flattened.
                var v2Id = ReadQualityId(quality);

                if (v2Id.HasValue)
                {
                    item["quality"] = V2QualityMap.QualityId(v2Id.Value);
                }

                var v5Id = ReadQualityId(item["quality"]);

                if (v5Id.HasValue && !known.Contains(v5Id.Value))
                {
                    items.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        private static void AddMissingQualities(JsonArray items)
        {
            var present = Flatten(items).ToHashSet();
            var insertAt = 0;

            foreach (var quality in Quality.All.Where(q => !present.Contains(q.Id)))
            {
                items.Insert(insertAt++, new JsonObject
                {
                    ["quality"] = quality.Id,
                    ["items"] = new JsonArray(),
                    ["allowed"] = false
                });
            }
        }

        private static IEnumerable<int> Flatten(JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                var id = ReadQualityId(item["quality"]);

                if (id.HasValue)
                {
                    yield return id.Value;
                }

                if (item["items"] is JsonArray children)
                {
                    foreach (var child in Flatten(children))
                    {
                        yield return child;
                    }
                }
            }
        }

        private static string FilterFormatItems(string json, HashSet<int> skippedFormats)
        {
            var items = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json).AsArray();

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var format = items[i]?["format"];
                var id = format is JsonObject formatObject ? ReadInt(formatObject["id"]) : ReadInt(format);

                if (id.HasValue && skippedFormats.Contains(id.Value))
                {
                    items.RemoveAt(i);
                }
            }

            return items.ToJsonString();
        }

        // ---- Quality definitions ----

        private static void PortQualityDefinitions(V2PortContext context)
        {
            context.V5.Execute("DELETE FROM \"QualityDefinitions\"", transaction: context.Transaction);

            var profiles = ReadRows(context, "QualityProfiles")
                .Select(r => (Id: Convert.ToInt32(r["Id"]), Name: Text(r, "Name"), Sizes: ReadSizes(Text(r, "Items"))))
                .ToList();

            var source = ChooseSizeSource(context, profiles.Select(p => p.Id).ToList());
            var sizes = profiles.FirstOrDefault(p => p.Id == source).Sizes ?? new Dictionary<int, double?[]>();

            foreach (var row in ReadRows(context, "QualityDefinitions"))
            {
                var v2Id = Convert.ToInt32(row["Quality"]);
                var v5Id = V2QualityMap.QualityId(v2Id);
                var quality = Quality.All.FirstOrDefault(q => q.Id == v5Id);

                if (quality == null)
                {
                    continue;
                }

                sizes.TryGetValue(v5Id, out var size);

                Insert(context, "QualityDefinitions", false, new Dictionary<string, object>
                {
                    ["Quality"] = v5Id,
                    ["Title"] = v5Id != v2Id ? quality.Name : Text(row, "Title"),
                    ["MinSize"] = size?[0],
                    ["MaxSize"] = size?[1],
                    ["PreferredSize"] = size?[2]
                });
            }

            var chosen = profiles.FirstOrDefault(p => p.Id == source);

            if (profiles.Count > 1 && chosen.Sizes != null && profiles.Any(p => p.Id != source && !SameSizes(p.Sizes, chosen.Sizes)))
            {
                context.Add(LegacyReportCategory.SkippedSetting, QualityArea, "QualityDefinitions", $"Sizes taken from profile {chosen.Name}");
            }
        }

        // The profile used by most series; the lowest Id breaks ties and covers the no-series case.
        private static int ChooseSizeSource(V2PortContext context, List<int> profileIds)
        {
            if (profileIds.Count == 0)
            {
                return 0;
            }

            var counts = context.Legacy
                .Query("SELECT \"QualityProfileId\" AS ProfileId, COUNT(*) AS Used FROM \"Series\" GROUP BY \"QualityProfileId\"")
                .Cast<IDictionary<string, object>>()
                .Select(r => (ProfileId: Convert.ToInt32(r["ProfileId"]), Used: Convert.ToInt64(r["Used"])))
                .Where(c => profileIds.Contains(c.ProfileId))
                .OrderByDescending(c => c.Used)
                .ThenBy(c => c.ProfileId)
                .ToList();

            return counts.Count > 0 ? counts[0].ProfileId : profileIds.Min();
        }

        // v5 quality id -> [min, max, preferred]
        private static Dictionary<int, double?[]> ReadSizes(string itemsJson)
        {
            var sizes = new Dictionary<int, double?[]>();

            CollectSizes(JsonNode.Parse(itemsJson ?? "[]").AsArray(), sizes);

            return sizes;
        }

        private static void CollectSizes(JsonArray items, Dictionary<int, double?[]> sizes)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                var v2Id = ReadQualityId(item["quality"]);

                if (v2Id.HasValue)
                {
                    sizes[V2QualityMap.QualityId(v2Id.Value)] = SizeProperties.Select(p => ReadDouble(item[p])).ToArray();
                }

                if (item["items"] is JsonArray children)
                {
                    CollectSizes(children, sizes);
                }
            }
        }

        private static bool SameSizes(Dictionary<int, double?[]> a, Dictionary<int, double?[]> b)
        {
            return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && kv.Value.SequenceEqual(other));
        }

        // ---- Custom formats ----

        private static HashSet<int> PortCustomFormats(V2PortContext context)
        {
            var skipped = new HashSet<int>();

            context.V5.Execute("DELETE FROM \"CustomFormats\"", transaction: context.Transaction);

            foreach (var row in ReadRows(context, "CustomFormats"))
            {
                var id = Convert.ToInt32(row["Id"]);
                var name = Text(row, "Name");
                var specs = JsonNode.Parse(Text(row, "Specifications") ?? "[]").AsArray();

                RemapSpecs(context, CustomFormatsArea, name, specs, CustomFormatMappers);

                if (specs.Count == 0)
                {
                    skipped.Add(id);
                    context.Add(LegacyReportCategory.SkippedSetting, CustomFormatsArea, name, "Custom format skipped: no specification has a v5 equivalent");
                    continue;
                }

                Insert(context, "CustomFormats", true, new Dictionary<string, object>
                {
                    ["Id"] = id,
                    ["Name"] = name,
                    ["Specifications"] = specs.ToJsonString(),
                    ["IncludeCustomFormatWhenRenaming"] = Bool(row, "IncludeCustomFormatWhenRenaming")
                });
            }

            context.InsertedTables.Add("CustomFormats");

            return skipped;
        }

        // Value remappers by spec type; a null result means the spec cannot be ported.
        private static readonly Dictionary<string, Func<int, int?>> CustomFormatMappers = new ()
        {
            ["SourceSpecification"] = v => V2QualityMap.QualitySource(v),
            ["LanguageSpecification"] = V2QualityMap.Language,
            ["IndexerFlagSpecification"] = V2QualityMap.IndexerFlag
        };

        private static readonly Dictionary<string, Func<int, int?>> AutoTaggingMappers = new ()
        {
            ["NetworkSpecification"] = _ => null,
            ["StatusSpecification"] = _ => null,
            ["OriginalLanguageSpecification"] = V2QualityMap.Language
        };

        // Remaps (or removes and reports) every spec of one format or rule. Specs of other types are copied.
        private static void RemapSpecs(V2PortContext context, string area, string subject, JsonArray specs, Dictionary<string, Func<int, int?>> mappers)
        {
            for (var i = specs.Count - 1; i >= 0; i--)
            {
                var type = (specs[i] as JsonObject)?["type"]?.GetValue<string>();

                if (type == null || !mappers.TryGetValue(type, out var mapper) || specs[i]["body"] is not JsonObject body)
                {
                    continue;
                }

                var v2Value = ReadInt(body["value"]);
                var v5Value = v2Value.HasValue ? mapper(v2Value.Value) : null;

                if (v5Value.HasValue)
                {
                    body["value"] = v5Value.Value;
                }
                else
                {
                    specs.RemoveAt(i);
                    context.Add(LegacyReportCategory.SkippedSetting, area, subject, $"{type} dropped: value {body["value"]} has no v5 equivalent");
                }
            }
        }

        // ---- Release profiles ----

        private static void PortReleaseProfiles(V2PortContext context)
        {
            context.V5.Execute("DELETE FROM \"ReleaseProfiles\"", transaction: context.Transaction);

            foreach (var row in ReadRows(context, "ReleaseProfiles"))
            {
                var id = Convert.ToInt32(row["Id"]);
                var name = Text(row, "Name");
                var indexerId = Convert.ToInt32(row["IndexerId"]);
                var subject = string.IsNullOrEmpty(name) ? $"Release profile {id}" : name;

                Insert(context, "ReleaseProfiles", true, new Dictionary<string, object>
                {
                    ["Id"] = id,
                    ["Name"] = name,
                    ["Enabled"] = Bool(row, "Enabled"),
                    ["Required"] = Text(row, "Required") ?? "[]",
                    ["Ignored"] = Text(row, "Ignored") ?? "[]",
                    ["Tags"] = Text(row, "Tags"),
                    ["IndexerIds"] = indexerId == 0 ? "[]" : $"[{indexerId}]"
                });

                var excluded = (Text(row, "ExcludedTags") ?? "[]").Replace(" ", string.Empty);

                if (excluded != "[]" && excluded.Length > 0)
                {
                    context.Add(LegacyReportCategory.SkippedSetting, ReleaseProfilesArea, subject, "Excluded tags have no v5 equivalent");
                }

                if (Bool(row, "AirDateRestriction"))
                {
                    context.Add(LegacyReportCategory.SkippedSetting, ReleaseProfilesArea, subject, "Air date restriction has no v5 equivalent");
                }
            }

            context.InsertedTables.Add("ReleaseProfiles");
        }

        // ---- Auto tagging ----

        private static void PortAutoTagging(V2PortContext context)
        {
            context.V5.Execute("DELETE FROM \"AutoTagging\"", transaction: context.Transaction);

            foreach (var row in ReadRows(context, "AutoTagging"))
            {
                var name = Text(row, "Name");
                var specs = JsonNode.Parse(Text(row, "Specifications") ?? "[]").AsArray();

                RemapSpecs(context, AutoTaggingArea, name, specs, AutoTaggingMappers);

                if (specs.Count == 0)
                {
                    context.Add(LegacyReportCategory.SkippedSetting, AutoTaggingArea, name, "Auto tagging rule skipped: no specification has a v5 equivalent");
                    continue;
                }

                Insert(context, "AutoTagging", true, new Dictionary<string, object>
                {
                    ["Id"] = Convert.ToInt32(row["Id"]),
                    ["Name"] = name,
                    ["Specifications"] = specs.ToJsonString(),
                    ["RemoveTagsAutomatically"] = Bool(row, "RemoveTagsAutomatically"),
                    ["Tags"] = Text(row, "Tags") ?? "[]"
                });
            }

            context.InsertedTables.Add("AutoTagging");
        }

        // ---- Helpers ----

        internal static List<IDictionary<string, object>> ReadRows(V2PortContext context, string table)
        {
            return context.Legacy.Query($"SELECT * FROM {Quote(table)} ORDER BY \"Id\"").Cast<IDictionary<string, object>>().ToList();
        }

        internal static void Insert(V2PortContext context, string table, bool explicitId, Dictionary<string, object> values)
        {
            // v5 PostgreSQL Ids are GENERATED ALWAYS, so explicit Ids need the override.
            var overriding = explicitId && context.V5Type == DatabaseType.PostgreSQL ? " OVERRIDING SYSTEM VALUE" : string.Empty;

            var parameters = new DynamicParameters();

            foreach (var (column, value) in values)
            {
                parameters.Add(column, value);
            }

            var columns = string.Join(", ", values.Keys.Select(Quote));
            var names = string.Join(", ", values.Keys.Select(k => "@" + k));

            context.V5.Execute($"INSERT INTO {Quote(table)} ({columns}){overriding} VALUES ({names})", parameters, context.Transaction);
        }

        internal static string Text(IDictionary<string, object> row, string column)
        {
            return row.TryGetValue(column, out var value) && value is not DBNull ? value as string : null;
        }

        // SQLite stores booleans as integers and PostgreSQL has a real type; either converts here.
        internal static bool Bool(IDictionary<string, object> row, string column)
        {
            return row.TryGetValue(column, out var value) && value != null && value is not DBNull && Convert.ToBoolean(value);
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

        private static double? ReadDouble(JsonNode node)
        {
            return node is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;
        }

        // A profile item's quality is an int, or an object carrying the id.
        private static int? ReadQualityId(JsonNode node)
        {
            return node is JsonObject quality ? ReadInt(quality["id"]) : ReadInt(node);
        }

        private static string Quote(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
