using System;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Movies.Jav
{
    public static class JavCode
    {
        // Optional leading digits (300MIUM), 2-6 letters, then 2-5 digits. FC2-PPV style ids do not match.
        private static readonly Regex CodeRegex = new(@"^(\d{0,4}[A-Za-z]{2,8})[-_ ]?(\d{2,8})$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        public static bool TryNormalize(string text, out string code)
        {
            code = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var match = CodeRegex.Match(text.Trim());

            if (!match.Success)
            {
                return false;
            }

            code = $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}";
            return true;
        }

        // A code at the very start of a release or file title, after optional [tags]. The code must end at a
        // non-alphanumeric so 'x264' / '1080p' / mid-title words never match. shortcut: no space separator
        // ('ABP 123.mp4'), since 'Some Movie 2020' would then read as a code; revisit if real files need it.
        private static readonly Regex LeadingCodeRegex = new(@"^(?:\s*\[[^\]]*\])*[\s._]*(\d{0,4}[A-Za-z]{2,8})[-_]?(\d{2,8})(?![A-Za-z0-9])", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        public static bool TryExtract(string title, out string code)
        {
            code = null;

            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var match = LeadingCodeRegex.Match(title);

            if (!match.Success)
            {
                return false;
            }

            code = $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}";
            return true;
        }

        public static string SearchVariant(string code)
        {
            return code.Replace("-", string.Empty);
        }
    }
}
