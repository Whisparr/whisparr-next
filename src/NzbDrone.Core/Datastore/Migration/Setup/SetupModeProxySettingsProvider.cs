using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Proxy;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // Setup mode has no config database to read proxy settings from, so requests made before the migration (the
    // Skyhook preflight) go out directly.
    public class SetupModeProxySettingsProvider : IHttpProxySettingsProvider
    {
        public HttpProxySettings GetProxySettings(HttpUri uri)
        {
            return null;
        }

        public HttpProxySettings GetProxySettings()
        {
            return null;
        }
    }
}
