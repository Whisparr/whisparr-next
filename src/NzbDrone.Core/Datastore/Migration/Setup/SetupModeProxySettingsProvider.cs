using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // Setup mode has no config database of its own, so the proxy comes from the legacy install's Config table, read-only
    // and once. The Skyhook preflight then goes out the same way the user's legacy install did. Eros wins when both
    // installs exist, as it does for config5.xml and the signed-in user.
    public class SetupModeProxySettingsProvider : IHttpProxySettingsProvider
    {
        private readonly ISetupModeState _setupModeState;
        private readonly Logger _logger;
        private readonly object _lock = new();

        private bool _loaded;
        private HttpProxySettings _settings;

        public SetupModeProxySettingsProvider(ISetupModeState setupModeState)
        {
            _setupModeState = setupModeState;
            _logger = NzbDroneLogger.GetLogger(this);
        }

        public HttpProxySettings GetProxySettings(HttpUri uri)
        {
            var proxySettings = GetProxySettings();

            if (proxySettings == null || HttpProxySettingsProvider.IsBypassed(proxySettings, uri))
            {
                return null;
            }

            return proxySettings;
        }

        public HttpProxySettings GetProxySettings()
        {
            lock (_lock)
            {
                if (!_loaded)
                {
                    _settings = Load();
                    _loaded = true;
                }

                return _settings;
            }
        }

        private HttpProxySettings Load()
        {
            var installs = _setupModeState.Detection.Installs;
            var source = (installs.FirstOrDefault(i => i.Kind == LegacyKind.Eros) ?? installs.FirstOrDefault())?.Database;

            if (source == null)
            {
                return null;
            }

            try
            {
                using var connection = source.Type == DatabaseType.SQLite
                    ? LegacyConnections.OpenSqliteReadOnly(source.SqlitePath)
                    : LegacyConnections.OpenPostgres(source.PostgresConnectionString);

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (key, value) in connection.Query<(string Key, string Value)>("SELECT \"Key\", \"Value\" FROM \"Config\""))
                {
                    values[key] = value;
                }

                return Build(values);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not read the proxy settings from the legacy database, requests go out directly");

                return null;
            }
        }

        // The same keys and defaults as ConfigService, and the same settings HttpProxySettingsProvider builds from them.
        private static HttpProxySettings Build(Dictionary<string, string> values)
        {
            if (!Bool(values, "proxyenabled", false))
            {
                return null;
            }

            var type = values.TryGetValue("proxytype", out var typeText) && Enum.TryParse<ProxyType>(typeText, true, out var parsedType)
                ? parsedType
                : ProxyType.Http;

            var port = values.TryGetValue("proxyport", out var portText) && int.TryParse(portText, out var parsedPort)
                ? parsedPort
                : 8080;

            return new HttpProxySettings(
                type,
                Text(values, "proxyhostname"),
                port,
                Text(values, "proxybypassfilter"),
                Bool(values, "proxybypasslocaladdresses", true),
                Text(values, "proxyusername"),
                Text(values, "proxypassword"));
        }

        private static string Text(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        }

        private static bool Bool(Dictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out var text) && bool.TryParse(text, out var parsed) ? parsed : fallback;
        }
    }
}
