using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Movies.Jav
{
    public interface IJavAvailability
    {
        bool IsAvailable();
    }

    public class JavAvailability : IJavAvailability
    {
        private readonly IConfigService _configService;

        public JavAvailability(IConfigService configService)
        {
            _configService = configService;
        }

        // JAV is only served by TPDB
        public bool IsAvailable()
        {
            return _configService.WhisparrSceneMetadataSource == SceneMetadataType.Tpdb;
        }
    }
}
