using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    [TestFixture]
    public class V2TokenMapFixture : TestBase
    {
        [TestCase("{Site Title}", "{Studio Title}")]
        [TestCase("{Site Title} - {Release-Date} - {Episode Title} [{Quality Full}]", "{Studio Title} - {Release-Date} - {Scene Title} [{Quality Full}]")]
        [TestCase("{site.title}", "{studio.title}")]
        [TestCase("{SITE_TITLE}", "{STUDIO_TITLE}")]
        [TestCase("{Episode Title:30}", "{Scene Title:30}")]
        [TestCase("{[Quality Full]}", "{[Quality Full]}")]
        [TestCase("{Episode PerformersFemale}", "{Scene PerformersFemale}")]
        [TestCase("{Site TitleWithoutYear}", "{Studio Title}")]
        [TestCase("{Site TitleTheWithoutYear}", "{Studio TitleThe}")]
        [TestCase("{MediaInfo VideoCodec} {Release Group} {Original Filename} {Custom Formats}", "{MediaInfo VideoCodec} {Release Group} {Original Filename} {Custom Formats}")]
        [TestCase("{{Site Title}}", "{{Site Title}}")]
        public void should_map_tokens(string v2, string v5)
        {
            var result = V2TokenMap.Map(v2);

            result.Format.Should().Be(v5);
            result.DroppedTokens.Should().BeEmpty();
        }

        [Test]
        public void should_drop_year_token_and_report()
        {
            var result = V2TokenMap.Map("{Site Title} ({Site Year})");

            result.Format.Should().Be("{Studio Title} ()");
            result.DroppedTokens.Should().Equal("Site Year");
        }

        [Test]
        public void should_collapse_spaces_after_drop()
        {
            var result = V2TokenMap.Map("{Site Title} {Episode Year} {Episode Title}");

            result.Format.Should().Be("{Studio Title} {Scene Title}");
            result.DroppedTokens.Should().Equal("Episode Year");
        }

        [Test]
        public void should_map_and_note_tpdb_scene_id()
        {
            var result = V2TokenMap.Map("{TpdbSceneId}");

            result.Format.Should().Be("{Scene ForeignId}");
            result.DroppedTokens.Should().HaveCount(1);
        }

        [Test]
        public void should_drop_unknown_tokens_with_prefix_and_suffix()
        {
            var result = V2TokenMap.Map("{Site Title} {-Foo Bar-} end");

            result.Format.Should().Be("{Studio Title} end");
            result.DroppedTokens.Should().Equal("Foo Bar");
        }

        [Test]
        public void should_handle_empty_format()
        {
            V2TokenMap.Map("").Format.Should().BeEmpty();
            V2TokenMap.Map(null).DroppedTokens.Should().BeEmpty();
        }
    }
}
