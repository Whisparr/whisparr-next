using System.Collections.Generic;
using NzbDrone.Common.Cache;

namespace NzbDrone.Core.Movies
{
    /// <summary>
    /// Names of the API resource caches, one per API version. Controllers name their caches
    /// after the resource type, and Core can't reference the API projects, so the names are spelled out here.
    /// </summary>
    public static class ApiResourceCaches
    {
        public static readonly IReadOnlyList<string> Movie = new[]
        {
            "Whisparr.Api.V3.Movies.MovieResource_movieResources",
            "Whisparr.Api.V5.Movies.MovieResource_movieResources"
        };

        public static readonly IReadOnlyList<string> Performer = new[]
        {
            "Whisparr.Api.V3.Performers.PerformerResource_performerResources",
            "Whisparr.Api.V5.Performers.PerformerResource_performerResources"
        };

        public static readonly IReadOnlyList<string> Studio = new[]
        {
            "Whisparr.Api.V3.Studios.StudioResource_studioResources",
            "Whisparr.Api.V5.Studios.StudioResource_studioResources"
        };

        /// <summary>Removes the key from every named cache that exists; caches not created yet are skipped.</summary>
        public static void Remove(this ICacheManager cacheManager, IEnumerable<string> cacheNames, string cacheKey)
        {
            foreach (var cacheName in cacheNames)
            {
                cacheManager.FindCache(cacheName)?.Remove(cacheKey);
            }
        }
    }
}
