using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    [TestFixture]
    public class V2QualityMapFixture : TestBase
    {
        [TestCase(13, 20)]
        [TestCase(23, 21)]
        [TestCase(20, 30)]
        [TestCase(21, 31)]
        [TestCase(22, 32)]
        [TestCase(7, 7)]
        public void should_map_quality_id(int v2, int v5)
        {
            V2QualityMap.QualityId(v2).Should().Be(v5);
        }

        [TestCase(0, 0)]
        [TestCase(1, 5)]
        [TestCase(2, 6)]
        [TestCase(3, 1)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(6, 7)]
        [TestCase(7, 10)]
        [TestCase(8, 8)]
        [TestCase(99, 0)]
        public void should_map_quality_source(int v2, int v5)
        {
            V2QualityMap.QualitySource(v2).Should().Be(v5);
        }

        [Test]
        public void should_map_language_by_name()
        {
            V2QualityMap.Language(26).Should().Be(31);
            V2QualityMap.Language(1).Should().Be(1);
        }

        [TestCase(47)]
        [TestCase(48)]
        [TestCase(49)]
        [TestCase(999)]
        public void should_return_null_for_missing_language(int v2)
        {
            V2QualityMap.Language(v2).Should().BeNull();
        }

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(4, 4)]
        [TestCase(8, 32)]
        [TestCase(16, 128)]
        [TestCase(32, 256)]
        [TestCase(64, 512)]
        [TestCase(128, 2048)]
        public void should_map_single_indexer_flag(int v2, int v5)
        {
            V2QualityMap.IndexerFlag(v2).Should().Be(v5);
        }

        [Test]
        public void should_return_null_for_subtitles_flag()
        {
            V2QualityMap.IndexerFlag(256).Should().BeNull();
        }

        [Test]
        public void should_remap_and_drop_subtitles_in_flag_set()
        {
            V2QualityMap.IndexerFlags(8 | 16 | 256).Should().Be(32 | 128);
        }
    }
}
