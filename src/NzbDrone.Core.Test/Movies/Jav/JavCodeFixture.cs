using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Jav;

namespace NzbDrone.Core.Test.MovieTests.Jav
{
    [TestFixture]
    public class JavCodeFixture
    {
        [TestCase("abp123", "ABP-123")]
        [TestCase("ABP 123", "ABP-123")]
        [TestCase("ABP_123", "ABP-123")]
        [TestCase("abp-123", "ABP-123")]
        [TestCase("300MIUM-1234", "300MIUM-1234")]
        public void should_normalize_code(string text, string expected)
        {
            JavCode.TryNormalize(text, out var code).Should().BeTrue();
            code.Should().Be(expected);
        }

        [TestCase("Momo Sakai")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("FC2-PPV-1234567")]
        public void should_reject_non_code(string text)
        {
            JavCode.TryNormalize(text, out var code).Should().BeFalse();
            code.Should().BeNull();
        }

        [Test]
        public void should_strip_dash_for_search_variant()
        {
            JavCode.SearchVariant("ABP-123").Should().Be("ABP123");
        }

        [Test]
        public void should_pin_enum_values()
        {
            ((int)ItemType.Jav).Should().Be(4);
            ((int)Core.Movies.MetadataSource.TpdbJav).Should().Be(4);
        }
    }
}
