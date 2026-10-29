using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    public class V2ConfigPorter
    {
        private const string ConfigArea = "Config";
        private const string NamingArea = "Naming";
        private const string FileDateKey = "filedate";
        private const string DefaultSceneFolderFormat = "{Studio Title}";

        // v2 develop NamingConfig defaults, for installs that never saved naming settings (no row at all).
        private const string V2DefaultEpisodeFormat = "{Site Title} - {Release-Date} - {Episode Title} [{Quality Full}]";
        private const string V2DefaultSeriesFolderFormat = "{Site Title}";
        private const string V2DefaultJavFormat = "{External Id}";

        // v2 stores ColonReplacementFormat 5 as Custom; v5 has no such value.
        private const int V2CustomColon = 5;
        private const int V5SmartColon = 4;

        // Keys the series-to-movie rename changed. Recon 2026-10-29, v2 develop c55fdc66a vs v5 ConfigService.
        private static readonly Dictionary<string, string> RenamedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["autounmonitorpreviouslydownloadedepisodes"] = "autounmonitorpreviouslydownloadedmovies",
            ["createemptyseriesfolders"] = "createemptymoviefolders"
        };

        // v2-only keys with no v5 reader. Recon 2026-10-29, v2 develop c55fdc66a vs v5 ConfigService.
        private static readonly HashSet<string> DroppedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "episodetitlerequired",
            "userrejectedextensions"
        };

        public void Port(V2PortContext context)
        {
            PortConfig(context);
            PortNaming(context);
        }

        private static void PortConfig(V2PortContext context)
        {
            var rows = context.Legacy.Query<(string Key, string Value)>("SELECT \"Key\", \"Value\" FROM \"Config\" ORDER BY \"Id\"").ToList();

            foreach (var (legacyKey, value) in rows)
            {
                if (DroppedKeys.Contains(legacyKey))
                {
                    context.Add(LegacyReportCategory.SkippedSetting, ConfigArea, legacyKey, value);
                    continue;
                }

                // v5 stores lowercase keys.
                var key = (RenamedKeys.TryGetValue(legacyKey, out var renamed) ? renamed : legacyKey).ToLowerInvariant();

                Upsert(context, key, key == FileDateKey ? MapFileDate(context, legacyKey, value) : value);
            }
        }

        // v2 FileDateType is None/LocalAirDate/UtcAirDate; v5's is None/Cinemas/Release and is read with Enum.Parse.
        private static string MapFileDate(V2PortContext context, string legacyKey, string value)
        {
            var mapped = (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "none" => "none",
                "localairdate" or "utcairdate" => "release",
                _ => "none"
            };

            if (!string.Equals(value, mapped, StringComparison.OrdinalIgnoreCase))
            {
                context.Add(LegacyReportCategory.SkippedSetting, ConfigArea, legacyKey, $"{value} → {mapped}");
            }

            return mapped;
        }

        private static void Upsert(V2PortContext context, string key, string value)
        {
            var ids = context.V5.Query<long>("SELECT \"Id\" FROM \"Config\" WHERE lower(\"Key\") = @key", new { key }, context.Transaction).ToList();

            if (ids.Count > 0)
            {
                context.V5.Execute("UPDATE \"Config\" SET \"Key\" = @key, \"Value\" = @value WHERE \"Id\" = @id", new { key, value, id = ids[0] }, context.Transaction);
            }
            else
            {
                context.V5.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES (@key, @value)", new { key, value }, context.Transaction);
            }
        }

        private static void PortNaming(V2PortContext context)
        {
            var legacy = context.Legacy.Query("SELECT * FROM \"NamingConfig\" ORDER BY \"Id\"").Cast<IDictionary<string, object>>().FirstOrDefault();

            var episodeFormat = V2DefaultEpisodeFormat;
            var seriesFolderFormat = V2DefaultSeriesFolderFormat;
            var replaceIllegal = true;
            var colon = V5SmartColon;

            if (legacy != null)
            {
                episodeFormat = Text(legacy, "StandardEpisodeFormat");
                seriesFolderFormat = Text(legacy, "SeriesFolderFormat");
                replaceIllegal = Convert.ToBoolean(legacy["ReplaceIllegalCharacters"]);
                colon = Convert.ToInt32(legacy["ColonReplacementFormat"]);

                if (colon == V2CustomColon)
                {
                    context.Add(LegacyReportCategory.SkippedSetting, NamingArea, "ColonReplacementFormat", Text(legacy, "CustomColonReplacementFormat"));
                    colon = V5SmartColon;
                }

                var jav = Text(legacy, "JavEpisodeFormat");

                if (!string.IsNullOrWhiteSpace(jav) && !string.Equals(jav.Trim(), V2DefaultJavFormat, StringComparison.Ordinal))
                {
                    context.Add(LegacyReportCategory.SkippedSetting, NamingArea, "JavEpisodeFormat", jav);
                }
            }

            if (string.IsNullOrEmpty(episodeFormat))
            {
                episodeFormat = V2DefaultEpisodeFormat;
                context.Add(LegacyReportCategory.NeedsAttention, NamingArea, "StandardSceneFormat", "The scene file format was empty, so the v2 default is used.");
            }

            var standard = Map(context, "StandardSceneFormat", episodeFormat);
            var folder = Map(context, "SceneFolderFormat", seriesFolderFormat);

            if (string.IsNullOrEmpty(folder))
            {
                folder = DefaultSceneFolderFormat;
                context.Add(LegacyReportCategory.NeedsAttention, NamingArea, "SceneFolderFormat", "The scene folder format was empty, so " + DefaultSceneFolderFormat + " is used.");
            }

            var defaults = NamingConfig.Default;

            context.V5.Execute("DELETE FROM \"NamingConfig\"", transaction: context.Transaction);

            context.V5.Execute(
                "INSERT INTO \"NamingConfig\" (\"RenameMovies\", \"RenameScenes\", \"ReplaceIllegalCharacters\", \"ColonReplacementFormat\", \"StandardMovieFormat\", \"StandardSceneFormat\", \"MovieFolderFormat\", \"SceneFolderFormat\", \"SceneImportFolderFormat\", \"MaxFolderPathLength\", \"MaxFilePathLength\") " +
                "VALUES (@RenameMovies, @RenameScenes, @ReplaceIllegalCharacters, @ColonReplacementFormat, @StandardMovieFormat, @StandardSceneFormat, @MovieFolderFormat, @SceneFolderFormat, @SceneImportFolderFormat, @MaxFolderPathLength, @MaxFilePathLength)",
                new
                {
                    RenameMovies = false,
                    RenameScenes = false,
                    ReplaceIllegalCharacters = replaceIllegal,
                    ColonReplacementFormat = colon,
                    defaults.StandardMovieFormat,
                    StandardSceneFormat = standard,
                    defaults.MovieFolderFormat,
                    SceneFolderFormat = folder,
                    defaults.SceneImportFolderFormat,
                    defaults.MaxFolderPathLength,
                    defaults.MaxFilePathLength
                },
                context.Transaction);
        }

        private static string Map(V2PortContext context, string field, string v2Format)
        {
            var result = V2TokenMap.Map(v2Format);

            foreach (var token in result.DroppedTokens)
            {
                context.Add(LegacyReportCategory.DroppedToken, NamingArea, field, token);
            }

            return result.Format;
        }

        private static string Text(IDictionary<string, object> row, string column)
        {
            return row.TryGetValue(column, out var value) && value is not DBNull ? value as string : null;
        }
    }
}
