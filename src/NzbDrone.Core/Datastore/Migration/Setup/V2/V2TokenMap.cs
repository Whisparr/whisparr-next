using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    public class TokenMapResult
    {
        public string Format { get; set; }
        public List<string> DroppedTokens { get; set; } = new ();
    }

    /// <summary>
    /// Translates v2 naming-format tokens to their v5 equivalents.
    /// </summary>
    public static class V2TokenMap
    {
        // Copied from Whisparrv2 FileNameBuilder.TitleRegex, with a match timeout added.
        private static readonly Regex TitleRegex = new (
            @"(?<escaped>\{\{|\}\})|\{(?<prefix>[- ._\[(]*)(?<token>(?:[a-z0-9]+)(?:(?<separator>[- ._]+)(?:[a-z0-9]+))?)(?::(?<customFormat>[a-z0-9+-]+(?<!-)))?(?<suffix>[- ._)\]]*)\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        private static readonly Regex MultiSpace = new (" {2,}", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

        // v2 "word1 word2" (lowercase, no separator) => v5 words.
        private static readonly Dictionary<string, string[]> Renames = new (StringComparer.OrdinalIgnoreCase)
        {
            { "site title", new[] { "Studio", "Title" } },
            { "site cleantitle", new[] { "Studio", "CleanTitle" } },
            { "site titleslug", new[] { "Studio", "TitleSlug" } },
            { "site titlethe", new[] { "Studio", "TitleThe" } },
            { "site titlefirstcharacter", new[] { "Studio", "TitleFirstCharacter" } },
            { "site network", new[] { "Studio", "Network" } },
            { "site titlewithoutyear", new[] { "Studio", "Title" } },
            { "site cleantitlewithoutyear", new[] { "Studio", "CleanTitle" } },
            { "site titlethewithoutyear", new[] { "Studio", "TitleThe" } },
            { "episode title", new[] { "Scene", "Title" } },
            { "episode cleantitle", new[] { "Scene", "CleanTitle" } },
            { "episode performers", new[] { "Scene", "Performers" } },
            { "episode performersfemale", new[] { "Scene", "PerformersFemale" } },
            { "episode performersmale", new[] { "Scene", "PerformersMale" } },
            { "episode performersother", new[] { "Scene", "PerformersOther" } },
            { "tpdbsceneid", new[] { "Scene", "ForeignId" } }
        };

        private static readonly HashSet<string> UnchangedExact = new (StringComparer.OrdinalIgnoreCase)
        {
            "release date",
            "release shortdate",
            "release group",
            "original title",
            "original filename",
            "custom formats"
        };

        private static readonly string[] UnchangedFirstWords = { "quality", "mediainfo" };

        public static TokenMapResult Map(string v2Format)
        {
            var result = new TokenMapResult { Format = v2Format };

            if (string.IsNullOrEmpty(v2Format))
            {
                return result;
            }

            var dropped = new List<string>();
            var anyDropped = false;

            var mapped = TitleRegex.Replace(v2Format, match =>
            {
                if (match.Groups["escaped"].Success)
                {
                    return match.Value;
                }

                var token = match.Groups["token"].Value;
                var separator = match.Groups["separator"].Success ? match.Groups["separator"].Value : string.Empty;
                var words = separator.Length > 0
                    ? token.Split(separator, 2, StringSplitOptions.None)
                    : new[] { token };
                var key = string.Join(' ', words).ToLowerInvariant();

                if (IsUnchanged(words, key))
                {
                    return match.Value;
                }

                if (!Renames.TryGetValue(key, out var v5Words))
                {
                    anyDropped = true;
                    AddDropped(dropped, token);
                    return string.Empty;
                }

                if (key == "tpdbsceneid")
                {
                    AddDropped(dropped, "TpdbSceneId (now the TPDB UUID)");
                }

                var style = DetectStyle(token);
                var sb = new StringBuilder("{");
                sb.Append(match.Groups["prefix"].Value);
                sb.Append(string.Join(separator.Length > 0 ? separator : " ", v5Words.Select(w => ApplyStyle(w, style))));

                if (match.Groups["customFormat"].Success)
                {
                    sb.Append(':').Append(match.Groups["customFormat"].Value);
                }

                sb.Append(match.Groups["suffix"].Value).Append('}');
                return sb.ToString();
            });

            if (anyDropped)
            {
                mapped = MultiSpace.Replace(mapped, " ").Trim();
            }

            result.Format = mapped;
            result.DroppedTokens = dropped;
            return result;
        }

        private static bool IsUnchanged(string[] words, string key)
        {
            if (UnchangedExact.Contains(key))
            {
                return true;
            }

            return words.Length == 2 && UnchangedFirstWords.Contains(words[0], StringComparer.OrdinalIgnoreCase);
        }

        private static void AddDropped(List<string> dropped, string entry)
        {
            if (!dropped.Contains(entry, StringComparer.Ordinal))
            {
                dropped.Add(entry);
            }
        }

        private static int DetectStyle(string token)
        {
            var letters = token.Where(char.IsLetter).ToList();

            if (letters.Count > 0 && letters.All(char.IsUpper))
            {
                return 1;
            }

            if (letters.Count > 0 && letters.All(char.IsLower))
            {
                return -1;
            }

            return 0;
        }

        private static string ApplyStyle(string word, int style)
        {
            return style switch
            {
                1 => word.ToUpperInvariant(),
                -1 => word.ToLowerInvariant(),
                _ => word
            };
        }
    }
}
