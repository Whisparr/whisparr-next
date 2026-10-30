using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.LegacyMigration
{
    public sealed record SceneKey(string ForeignId, string Title, DateTime? ReleaseDate);

    // Exact title-and-date identification against a scene catalog; ambiguity is never guessed at.
    public static class LegacySceneMatcher
    {
        public static string FindExact(IEnumerable<SceneKey> catalog, string title, DateTime? releaseDate)
        {
            if (title.IsNullOrWhiteSpace() || !releaseDate.HasValue)
            {
                return null;
            }

            var cleanTitle = title.CleanMovieTitle();
            var day = releaseDate.Value.Date;

            var ids = catalog
                .Where(s => s.ReleaseDate.HasValue &&
                            s.ReleaseDate.Value.Date == day &&
                            s.Title.IsNotNullOrWhiteSpace() &&
                            s.Title.CleanMovieTitle() == cleanTitle)
                .Select(s => s.ForeignId)
                .Distinct()
                .Take(2)
                .ToList();

            return ids.Count == 1 ? ids[0] : null;
        }
    }
}
