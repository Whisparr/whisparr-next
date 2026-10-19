using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Movies
{
    // TPDB-sourced studios and performers list TPDB movies only, so their movie sync needs TPDB as the movie source.
    public static class TpdbMovieSync
    {
        public const string NeedsTpdbMovieSourceMessage = "Movie sync for TPDB-sourced studios and performers needs TPDB as the movie source";

        public static bool IsBlocked(IConfigService configService)
        {
            return configService.WhisparrSceneMetadataSource == SceneMetadataType.Tpdb &&
                   configService.WhisparrMovieMetadataSource != MovieMetadataType.TPDB;
        }
    }
}
