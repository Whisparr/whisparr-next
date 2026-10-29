using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    /// <summary>
    /// Pure translation tables from Whisparr v2 numeric values to their v5 equivalents.
    /// </summary>
    public static class V2QualityMap
    {
        private static readonly Dictionary<int, int> QualityIds = new ()
        {
            { 13, 20 }, // Bluray-480p
            { 23, 21 }, // Bluray-576p
            { 20, 30 }, // Remux-1080p
            { 21, 31 }, // Remux-2160p
            { 22, 32 }  // VR
        };

        private static readonly Dictionary<int, int> QualitySources = new ()
        {
            { 0, 0 },  // Unknown
            { 1, 5 },  // Television
            { 2, 6 },  // TelevisionRaw
            { 3, 1 },  // Web
            { 4, 2 },  // WebRip
            { 5, 3 },  // DVD
            { 6, 7 },  // Bluray
            { 7, 10 }, // VR
            { 8, 8 }   // BlurayRaw
        };

        private static readonly Dictionary<int, int> IndexerFlagMap = new ()
        {
            { 1, 1 },
            { 2, 2 },
            { 4, 4 },
            { 8, 32 },    // Internal
            { 16, 128 },  // Scene
            { 32, 256 },  // Freeleech75
            { 64, 512 },  // Freeleech25
            { 128, 2048 } // Nuked
        };

        // (id, name) pairs copied from Whisparrv2 src/NzbDrone.Core/Languages/Language.cs
        private static readonly (int Id, string Name)[] V2Languages =
        {
            (0, "Unknown"),
            (1, "English"),
            (2, "French"),
            (3, "Spanish"),
            (4, "German"),
            (5, "Italian"),
            (6, "Danish"),
            (7, "Dutch"),
            (8, "Japanese"),
            (9, "Icelandic"),
            (10, "Chinese"),
            (11, "Russian"),
            (12, "Polish"),
            (13, "Vietnamese"),
            (14, "Swedish"),
            (15, "Norwegian"),
            (16, "Finnish"),
            (17, "Turkish"),
            (18, "Portuguese"),
            (19, "Flemish"),
            (20, "Greek"),
            (21, "Korean"),
            (22, "Hungarian"),
            (23, "Hebrew"),
            (24, "Lithuanian"),
            (25, "Czech"),
            (26, "Arabic"),
            (27, "Hindi"),
            (28, "Bulgarian"),
            (29, "Malayalam"),
            (30, "Ukrainian"),
            (31, "Slovak"),
            (32, "Thai"),
            (33, "Portuguese (Brazil)"),
            (34, "Spanish (Latino)"),
            (35, "Romanian"),
            (36, "Latvian"),
            (37, "Persian"),
            (38, "Catalan"),
            (39, "Croatian"),
            (40, "Serbian"),
            (41, "Bosnian"),
            (42, "Estonian"),
            (43, "Tamil"),
            (44, "Indonesian"),
            (45, "Macedonian"),
            (46, "Slovenian"),
            (47, "Azerbaijani"),
            (48, "Uzbek"),
            (49, "Malay"),
            (50, "Urdu"),
            (51, "Romansh"),
            (52, "Georgian"),
            (-2, "Original")
        };

        private static readonly Lazy<Dictionary<int, int>> LanguageMap = new (BuildLanguageMap);

        public static int QualityId(int v2Id)
        {
            return QualityIds.TryGetValue(v2Id, out var v5Id) ? v5Id : v2Id;
        }

        public static int QualitySource(int v2Value)
        {
            return QualitySources.TryGetValue(v2Value, out var v5Value) ? v5Value : 0;
        }

        public static int? Language(int v2Id)
        {
            return LanguageMap.Value.TryGetValue(v2Id, out var v5Id) ? v5Id : null;
        }

        public static int? IndexerFlag(int v2Flag)
        {
            return IndexerFlagMap.TryGetValue(v2Flag, out var v5Flag) ? v5Flag : null;
        }

        public static int IndexerFlags(int v2Flags)
        {
            var result = 0;

            foreach (var (v2Flag, v5Flag) in IndexerFlagMap)
            {
                if ((v2Flags & v2Flag) == v2Flag)
                {
                    result |= v5Flag;
                }
            }

            return result;
        }

        private static Dictionary<int, int> BuildLanguageMap()
        {
            var v5ByName = Languages.Language.All
                .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            var map = new Dictionary<int, int>();

            foreach (var (id, name) in V2Languages)
            {
                if (v5ByName.TryGetValue(name, out var v5Id))
                {
                    map[id] = v5Id;
                }
            }

            return map;
        }
    }
}
