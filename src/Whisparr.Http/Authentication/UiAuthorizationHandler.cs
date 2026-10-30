using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Messaging.Events;
using Whisparr.Http.Extensions;
using Whisparr.Http.Middleware;

namespace NzbDrone.Http.Authentication
{
    public class UiAuthorizationHandler : AuthorizationHandler<BypassableDenyAnonymousAuthorizationRequirement>, IAuthorizationRequirement, IHandle<ConfigSavedEvent>
    {
        private readonly IConfigFileProvider _configService;
        private static AuthenticationRequiredType _authenticationRequired;

        private readonly ISetupModeState _setupModeState;

        public UiAuthorizationHandler(IConfigFileProvider configService, ISetupModeState setupModeState)
        {
            _configService = configService;
            _setupModeState = setupModeState;
            _authenticationRequired = configService.AuthenticationRequired;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BypassableDenyAnonymousAuthorizationRequirement requirement)
        {
            // A legacy install with no one to sign in as would lock its own local user out of the setup page.
            // SetupModeMiddleware already turns non-local requests away in that case.
            if (_setupModeState.IsActive &&
                !_setupModeState.Detection.HasUsableCredentials &&
                context.Resource is HttpContext setupContext &&
                SetupModeMiddleware.IsLocal(setupContext))
            {
                context.Succeed(requirement);

                return Task.CompletedTask;
            }

            if (_authenticationRequired == AuthenticationRequiredType.DisabledForLocalAddresses)
            {
                if (context.Resource is HttpContext httpContext &&
                    IPAddress.TryParse(httpContext.GetRemoteIP(), out var ipAddress) &&
                    IsClientAddressKnown(httpContext))
                {
                    if (ipAddress.IsLocalAddress() ||
                        (_configService.TrustCgnatIpAddresses && ipAddress.IsCgnatIpAddress()))
                    {
                        context.Succeed(requirement);
                    }
                }
            }
            else if (_authenticationRequired == AuthenticationRequiredType.DisabledForLocalhost)
            {
                if (context.Resource is HttpContext httpContext &&
                    IPAddress.TryParse(httpContext.GetRemoteIP(), out var ipAddress) &&
                    IsClientAddressKnown(httpContext) &&
                    IPAddress.IsLoopback(ipAddress))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }

        private static bool IsClientAddressKnown(HttpContext httpContext)
        {
            return !httpContext.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName);
        }

        public void Handle(ConfigSavedEvent message)
        {
            _authenticationRequired = _configService.AuthenticationRequired;
        }
    }
}
