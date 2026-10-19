using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications.Stash;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.StashTests
{
    [TestFixture]
    public class StashFixture : CoreTest<Stash>
    {
        [TestCase(SceneMetadataType.StashDb, "https://stashdb.org/graphql")]
        [TestCase(SceneMetadataType.Tpdb, "https://theporndb.net/graphql")]
        public void should_default_stash_box_endpoint_to_the_scene_source(SceneMetadataType source, string endpoint)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrSceneMetadataSource)
                  .Returns(source);

            var definition = Subject.DefaultDefinitions.Single();

            definition.Implementation.Should().Be(nameof(Stash));
            definition.ConfigContract.Should().Be(nameof(StashSettings));
            ((StashSettings)definition.Settings).StashBoxEndpoint.Should().Be(endpoint);
        }
    }
}
