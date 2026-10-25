using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore.Migration.Setup;
using Whisparr.Http.Extensions;

namespace Whisparr.Http.Middleware
{
    // While a legacy install waits to be migrated v5 has no database, so only the setup API and the UI that
    // drives it are served. Without a legacy user to sign in as, only the local network may reach it at all.
    public class SetupModeMiddleware
    {
        private const string SetupApiPrefix = "/api/setup";
        private const string ForbiddenMessage = "Whisparr is in setup mode and the legacy install has no user to sign in with, so it can only be reached from the local network.";

        private static readonly string[] BlockedPrefixes = { "/signalr", "/feed", "/docs" };

        private readonly RequestDelegate _next;
        private readonly ISetupModeState _setupModeState;

        public SetupModeMiddleware(RequestDelegate next, ISetupModeState setupModeState)
        {
            _next = next;
            _setupModeState = setupModeState;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_setupModeState.IsActive)
            {
                await _next(context);

                return;
            }

            if (!HasUsableCredentials() && !IsLocal(context))
            {
                await Write(context, 403, "text/plain", ForbiddenMessage);

                return;
            }

            if (IsBlocked(context.Request))
            {
                await Write(context, 503, "application/json", "{\"setupMode\":true}");

                return;
            }

            await _next(context);
        }

        private bool HasUsableCredentials()
        {
            return _setupModeState.Detection.Installs.Any(i => i.HasUsableCredentials);
        }

        private static bool IsBlocked(HttpRequest request)
        {
            if (request.Path.StartsWithSegments(SetupApiPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return request.IsApiRequest() || BlockedPrefixes.Any(p => request.Path.StartsWithSegments(p, System.StringComparison.OrdinalIgnoreCase));
        }

        // The same test the "disabled for local addresses" sign-in bypass uses, which also distrusts any
        // request still carrying an unconsumed X-Forwarded-For.
        private static bool IsLocal(HttpContext context)
        {
            return IPAddress.TryParse(context.GetRemoteIP(), out var address) &&
                   !context.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName) &&
                   address.IsLocalAddress();
        }

        private static async Task Write(HttpContext context, int statusCode, string contentType, string body)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;

            await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(body));
        }
    }
}
