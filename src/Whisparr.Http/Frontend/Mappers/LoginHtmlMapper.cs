using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Whisparr.Http.Frontend.Mappers
{
    public class LoginHtmlMapper : HtmlMapperBase
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly string _folderPath;

        public LoginHtmlMapper(IAppFolderInfo appFolderInfo,
                               IDiskProvider diskProvider,
                               Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                               IConfigFileProvider configFileProvider,
                               Logger logger)
            : base(diskProvider, cacheBreakProviderFactory, logger)
        {
            _configFileProvider = configFileProvider;
            _folderPath = appFolderInfo.GetUiFolder();

            HtmlPath = Path.Combine(_folderPath, "login.html");
            UrlBase = configFileProvider.UrlBase;
        }

        protected override string FolderPath => _folderPath;

        protected override string MapPath(string resourceUrl)
        {
            return HtmlPath;
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.StartsWith("/login");
        }

        protected override string GetHtmlText()
        {
            var html = base.GetHtmlText();
            var theme = _configFileProvider.Theme;

            var authMethod = _configFileProvider.AuthenticationMethod.ToString().ToLowerInvariant();

            if (_configFileProvider.AuthenticationMethod == AuthenticationType.Oidc &&
                !_configFileProvider.IsOidcConfigured())
            {
                authMethod += " oidc-misconfigured";
            }

            html = html.Replace("_THEME_", theme);
            html = html.Replace("_AUTH_METHOD_", authMethod);

            return html;
        }
    }
}
