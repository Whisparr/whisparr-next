using System;
using System.Collections.Generic;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    // Scenes, studios and performers are keyed by the locked scene source; movies keep their own ids.
    public static class SceneSourceIds
    {
        private const string TpdbRoutePrefix = "tpdb/";

        private static readonly HashSet<string> SceneRouteStems = new(StringComparer.Ordinal)
        {
            "scene",
            "site",
            "performer"
        };

        public static string Key(SceneMetadataType source, ExternalIdResource ids)
        {
            var key = source == SceneMetadataType.Tpdb ? ids?.TpdbId : ids?.StashId;

            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        // Only scene-side stems move under tpdb/; movie routes and anything already prefixed pass through.
        public static string Route(SceneMetadataType source, string stashRoute)
        {
            if (source != SceneMetadataType.Tpdb || string.IsNullOrEmpty(stashRoute))
            {
                return stashRoute;
            }

            var slash = stashRoute.IndexOf('/');
            var stem = slash < 0 ? stashRoute : stashRoute.Substring(0, slash);

            return SceneRouteStems.Contains(stem) ? TpdbRoutePrefix + stashRoute : stashRoute;
        }

        public static Movies.MetadataSource SceneMetadataSource(SceneMetadataType source)
        {
            return source == SceneMetadataType.Tpdb ? Movies.MetadataSource.TpdbScene : Movies.MetadataSource.Stash;
        }
    }
}
