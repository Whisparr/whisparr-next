using System;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public interface ISkyhookPreflight
    {
        void Check();
    }

    // Confirms Skyhook answers before the migration commits to a metadata source. Asks for the cheapest route
    // SkyHookProxy uses, built the same way.
    public class SkyhookPreflight : ISkyhookPreflight
    {
        private const string RouteConst = "route";
        private const string SinceConst = "since";

        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _whisparrMetadata;

        public SkyhookPreflight(IHttpClient httpClient, IWhisparrCloudRequestBuilder requestBuilder)
        {
            _httpClient = httpClient;
            _whisparrMetadata = requestBuilder.WhisparrMetadata;
        }

        public void Check()
        {
            var since = DateTime.UtcNow.AddHours(-1).ToString("s");

            var request = _whisparrMetadata.Create()
                .SetSegment(RouteConst, "movie/changed")
                .AddQueryParam(SinceConst, since)
                .Build();

            request.AllowAutoRedirect = true;
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            HttpResponse response;

            try
            {
                response = _httpClient.Get(request);
            }
            catch (Exception ex)
            {
                throw new SetupStepException($"Skyhook didn't answer: {ex.Message}", ex);
            }

            var status = (int)response.StatusCode;

            if (status < 200 || status > 299)
            {
                throw new SetupStepException($"Skyhook didn't answer: HTTP {status} ({response.StatusCode}).");
            }
        }
    }
}
