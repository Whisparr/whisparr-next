using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.LibrarySearch
{
    public enum LibrarySearchTier
    {
        Exact = 0,
        Prefix = 1,
        WordStart = 2,
        Substring = 3
    }

    public readonly record struct LibrarySearchScore(LibrarySearchTier Tier, int Distance);

    /// <summary>Orders library matches by how well their title fits the query.</summary>
    /// <remarks>
    /// The database only says a clean title contains the clean query. A tier ranks the kind of hit
    /// (whole title, start of title, start of a word, anywhere), and Levenshtein distance breaks
    /// ties within a tier. Distance alone sinks every long scene title below any short one.
    /// </remarks>
    public static class LibrarySearchRanker
    {
        /// <summary>Uppercase letters and digits only, so GAR-204, gar204 and "gar 204" all become GAR204.</summary>
        public static string NormalizeCode(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var normalized = new StringBuilder(value.Length);

            foreach (var c in value.Where(char.IsLetterOrDigit))
            {
                normalized.Append(char.ToUpperInvariant(c));
            }

            return normalized.ToString();
        }

        public static LibrarySearchScore Score(string query, string cleanQuery, string title, string cleanTitle, string foreignId, Func<string, string> clean, string code = null)
        {
            var titleScore = ScoreTitle(query, cleanQuery, title, cleanTitle, foreignId, clean);
            var codeScore = ScoreCode(query, code);

            return codeScore.HasValue && (codeScore.Value.Tier, codeScore.Value.Distance).CompareTo((titleScore.Tier, titleScore.Distance)) < 0
                ? codeScore.Value
                : titleScore;
        }

        // An exact code is the whole code, a prefix is the start of it (the query GAR lists every GAR code).
        private static LibrarySearchScore? ScoreCode(string query, string code)
        {
            var normalizedQuery = NormalizeCode(query);
            var normalizedCode = NormalizeCode(code);

            if (normalizedQuery.Length == 0 || normalizedCode.Length == 0)
            {
                return null;
            }

            if (normalizedCode == normalizedQuery)
            {
                return new LibrarySearchScore(LibrarySearchTier.Exact, 0);
            }

            return normalizedCode.StartsWith(normalizedQuery, StringComparison.Ordinal)
                ? new LibrarySearchScore(LibrarySearchTier.Prefix, normalizedCode.Length - normalizedQuery.Length)
                : null;
        }

        private static LibrarySearchScore ScoreTitle(string query, string cleanQuery, string title, string cleanTitle, string foreignId, Func<string, string> clean)
        {
            cleanTitle ??= string.Empty;

            if (cleanTitle == cleanQuery || (foreignId.IsNotNullOrWhiteSpace() && foreignId == query))
            {
                return new LibrarySearchScore(LibrarySearchTier.Exact, 0);
            }

            var distance = cleanQuery.LevenshteinDistance(cleanTitle);

            if (cleanTitle.StartsWith(cleanQuery, StringComparison.Ordinal))
            {
                return new LibrarySearchScore(LibrarySearchTier.Prefix, distance);
            }

            if (StartsAWord(cleanQuery, title, clean))
            {
                return new LibrarySearchScore(LibrarySearchTier.WordStart, distance);
            }

            return new LibrarySearchScore(LibrarySearchTier.Substring, distance);
        }

        public static List<T> Rank<T>(IEnumerable<T> items, string query, Func<T, string> title, Func<T, string> cleanTitle, Func<T, string> foreignId, Func<string, string> clean, Func<T, string> code = null)
        {
            var cleanQuery = clean(query);

            return items
                .Select(item => (Item: item, Title: title(item) ?? string.Empty, Score: Score(query, cleanQuery, title(item), cleanTitle(item), foreignId(item), clean, code?.Invoke(item))))
                .OrderBy(x => x.Score.Tier)
                .ThenBy(x => x.Score.Distance)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Item)
                .ToList();
        }

        // Clean titles have their spaces stripped, so word breaks come from the raw title. The
        // tokens from each word onward are joined, which lets a multi-word query match there too.
        private static bool StartsAWord(string cleanQuery, string title, Func<string, string> clean)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return false;
            }

            var words = SplitWords(title).Select(clean).Where(w => w.Length > 0).ToList();

            for (var i = 1; i < words.Count; i++)
            {
                var rest = new StringBuilder();

                for (var j = i; j < words.Count && rest.Length < cleanQuery.Length; j++)
                {
                    rest.Append(words[j]);
                }

                if (rest.ToString().StartsWith(cleanQuery, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> SplitWords(string title)
        {
            var word = new StringBuilder();

            foreach (var c in title)
            {
                if (char.IsLetterOrDigit(c))
                {
                    word.Append(c);
                }
                else if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }
            }

            if (word.Length > 0)
            {
                yield return word.ToString();
            }
        }
    }
}
