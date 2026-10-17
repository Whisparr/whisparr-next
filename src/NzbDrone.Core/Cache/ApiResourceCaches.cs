using System.Collections.Generic;
using NzbDrone.Common.Cache;

namespace NzbDrone.Core.Cache
{
    /// <summary>
    /// Names of the API resource caches, one per API version. Controllers name their caches
    /// after the resource type, and Core can't reference the API projects, so the names are spelled out here.
    /// </summary>
    public static class ApiResourceCaches
    {
        public static readonly string[] Movie =
        {
            "Whisparr.Api.V3.Movies.MovieResource_movieResources",
            "Whisparr.Api.V5.Movies.MovieResource_movieResources"
        };

        public static readonly string[] Performer =
        {
            "Whisparr.Api.V3.Performers.PerformerResource_performerResources",
            "Whisparr.Api.V5.Performers.PerformerResource_performerResources"
        };

        public static readonly string[] Studio =
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
