namespace NzbDrone.Core.Movies
{
    // A scene's ForeignId is a UUID from the source it came from, so its public page depends on
    // MetadataSource. Movies have no scene page.
    public static class SceneSourceLinks
    {
        public static string GetSceneUrl(MovieMetadata metadata)
        {
            return metadata?.MetadataSource switch
            {
                MetadataSource.Stash => "https://stashdb.org/scenes/" + metadata.ForeignId,
                MetadataSource.TpdbScene => "https://theporndb.net/scenes/" + metadata.ForeignId,
                _ => null
            };
        }

        public static string GetSceneSiteName(MovieMetadata metadata)
        {
            return metadata?.MetadataSource switch
            {
                MetadataSource.Stash => "StashDB",
                MetadataSource.TpdbScene => "TPDB",
                _ => null
            };
        }
    }
}
