using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.Jav
{
    [TestFixture]
    public class JavAvailabilityFixture : CoreTest<JavAvailability>
    {
        [TestCase(SceneMetadataType.Tpdb, true)]
        [TestCase(SceneMetadataType.StashDb, false)]
        public void should_be_available_only_for_tpdb_scene_source(SceneMetadataType source, bool expected)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.WhisparrSceneMetadataSource).Returns(source);

            Subject.IsAvailable().Should().Be(expected);
        }
    }
}
