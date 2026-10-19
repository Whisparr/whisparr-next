using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;

namespace Whisparr.Api.V5.Config
{
    public class MetadataSourceResource
    {
        public SceneMetadataType SceneMetadataSource { get; set; }
        public MovieMetadataType MovieMetadataSource { get; set; }
        public bool Locked { get; set; }
    }

    public static class MetadataSourceResourceMapper
    {
        public static MetadataSourceResource ToResource(this MetadataSources model)
        {
            return new MetadataSourceResource
            {
                SceneMetadataSource = model.Scene,
                MovieMetadataSource = model.Movie,
                Locked = model.Locked
            };
        }
    }
}
