using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using Whisparr.Http.Middleware;

namespace NzbDrone.Api.Test.Middleware
{
    [TestFixture]
    public class SetupModeMiddlewareFixture
    {
        private bool _nextCalled;

        private static ISetupModeState GivenSetupMode(bool usableCredentials)
        {
            var detection = new DetectionResult { SetupModeRequired = true };
            detection.Installs.Add(new LegacyInstall { Kind = LegacyKind.Eros, HasUsableCredentials = usableCredentials });

            return new SetupModeState(detection, false);
        }

        private async Task<DefaultHttpContext> Send(ISetupModeState state, string path, string remoteIp = "127.0.0.1")
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Response.Body = new MemoryStream();

            _nextCalled = false;

            var middleware = new SetupModeMiddleware(
                _ =>
                {
                    _nextCalled = true;

                    return Task.CompletedTask;
                },
                state);

            await middleware.InvokeAsync(context);

            return context;
        }

        private static string Body(HttpContext context)
        {
            context.Response.Body.Position = 0;

            return new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();
        }

        [TestCase("/api/v5/system/status")]
        [TestCase("/api/v3/movie")]
        [TestCase("/api/setupx/status")]
        [TestCase("/signalr/messages")]
        [TestCase("/feed/v3/calendar/whisparr.ics")]
        [TestCase("/docs/v5/openapi.json")]
        public async Task should_block_api_in_setup_mode(string path)
        {
            var context = await Send(GivenSetupMode(true), path);

            _nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(503);
            context.Response.ContentType.Should().Be("application/json");
            Body(context).Should().Be("{\"setupMode\":true}");
        }

        [TestCase("/api/setup/status")]
        [TestCase("/api/setup/start")]
        public async Task should_allow_setup_api(string path)
        {
            var context = await Send(GivenSetupMode(true), path);

            _nextCalled.Should().BeTrue();
            context.Response.StatusCode.Should().Be(200);
        }

        [Test]
        public async Task should_allow_non_api_paths_in_setup_mode()
        {
            await Send(GivenSetupMode(true), "/initialize.json");

            _nextCalled.Should().BeTrue();
        }

        [TestCase("8.8.8.8")]
        [TestCase("2001:4860:4860::8888")]
        public async Task should_forbid_remote_without_credentials(string remoteIp)
        {
            var context = await Send(GivenSetupMode(false), "/", remoteIp);

            _nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(403);
            context.Response.ContentType.Should().Be("text/plain");
            Body(context).Should().NotBeNullOrWhiteSpace();
        }

        [TestCase("127.0.0.1")]
        [TestCase("::1")]
        [TestCase("192.168.1.20")]
        [TestCase("10.0.0.5")]
        public async Task should_allow_local_without_credentials(string remoteIp)
        {
            await Send(GivenSetupMode(false), "/", remoteIp);

            _nextCalled.Should().BeTrue();
        }

        [Test]
        public async Task should_forbid_proxied_request_without_credentials()
        {
            var state = GivenSetupMode(false);
            var context = new DefaultHttpContext();
            context.Request.Path = "/";
            context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
            context.Request.Headers["X-Forwarded-For"] = "8.8.8.8";
            context.Response.Body = new MemoryStream();

            await new SetupModeMiddleware(_ => Task.CompletedTask, state).InvokeAsync(context);

            context.Response.StatusCode.Should().Be(403);
        }

        [Test]
        public async Task should_allow_remote_when_an_install_has_credentials()
        {
            await Send(GivenSetupMode(true), "/", "8.8.8.8");

            _nextCalled.Should().BeTrue();
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public async Task should_gate_a_pending_env_switch_on_the_users_of_the_v5_database(bool usableCredentials, bool reachable)
        {
            // No install is listed for a pending env switch, so the install flags can't decide.
            var detection = new DetectionResult { SetupModeRequired = true, PendingEnvSwitch = true, PendingSwitchHasUsableCredentials = usableCredentials };
            var context = await Send(new SetupModeState(detection, false), "/", "8.8.8.8");

            _nextCalled.Should().Be(reachable);
            context.Response.StatusCode.Should().Be(reachable ? 200 : 403);
        }

        [TestCase("/api/v5/system/status", "8.8.8.8")]
        [TestCase("/", "8.8.8.8")]
        [TestCase("/signalr/messages", "127.0.0.1")]
        public async Task should_pass_everything_in_normal_mode(string path, string remoteIp)
        {
            var context = await Send(SetupModeState.Inactive, path, remoteIp);

            _nextCalled.Should().BeTrue();
            context.Response.StatusCode.Should().Be(200);
        }
    }
}
