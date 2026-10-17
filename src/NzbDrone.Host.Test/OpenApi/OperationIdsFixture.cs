using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Host.OpenApi;

namespace NzbDrone.Host.Test.OpenApi
{
    [TestFixture]
    public class OperationIdsFixture
    {
        [TestCase("GET", "api/v3/tag/{id}", "getTagById")]
        [TestCase("GET", "api/v5/tag/{id}", "getTagById")]
        [TestCase("POST", "api/v5/movie/import", "postMovieImport")]
        public void should_build_the_same_id_for_each_api_version(string method, string path, string expected)
            => OperationIds.FromRoute(method, path).Should().Be(expected);
    }
}
