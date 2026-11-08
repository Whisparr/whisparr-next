using System;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Movies.Jav
{
    public static class JavCode
    {
        // Optional leading digits (300MIUM), 2-6 letters, then 2-5 digits. FC2-PPV style ids do not match.
        private static readonly Regex CodeRegex = new(@"^\s*(\d{0,3}[A-Za-z]{2,6})[-_ ]?(\d{2,5})\s*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        public static bool TryNormalize(string text, out string code)
        {
            code = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var match = CodeRegex.Match(text);

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
