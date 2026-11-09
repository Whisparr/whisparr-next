using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;

namespace Whisparr.Api.V5.Config
{
    public class MetadataSourceResource
    {
        // Nullable so a PUT that omits a source is rejected instead of locking the enum default.
        public SceneMetadataType? SceneMetadataSource { get; set; }
        public MovieMetadataType? MovieMetadataSource { get; set; }
        public bool Locked { get; set; }

        // JAV is only served by TPDB
        public bool JavAvailable { get; set; }
    }

    public static class MetadataSourceResourceMapper
    {
        public static MetadataSourceResource ToResource(this MetadataSources model)
        {
            return new MetadataSourceResource
            {
                SceneMetadataSource = model.Scene,
                MovieMetadataSource = model.Movie,
                Locked = model.Locked,
                JavAvailable = model.Scene == SceneMetadataType.Tpdb
            };
        }
    }
}
