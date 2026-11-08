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
        [TestCase("1234ABC-123", "1234ABC-123")]
        [TestCase("abcdefgh-123", "ABCDEFGH-123")]
        [TestCase("ABC-12345678", "ABC-12345678")]
        [TestCase("  abp123 ", "ABP-123")]
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

        [TestCase("abp123.mp4", "ABP-123")]
        [TestCase("ABP123.720p", "ABP-123")]
        [TestCase("ABP_123 Momo Sakai.mkv", "ABP-123")]
        [TestCase("abp-123 1080p.mp4", "ABP-123")]
        [TestCase("[FHD] abp123.mkv", "ABP-123")]
        [TestCase("300MIUM-1234.mp4", "300MIUM-1234")]
        public void should_extract_leading_code(string title, string expected)
        {
            JavCode.TryExtract(title, out var code).Should().BeTrue();
            code.Should().Be(expected);
        }

        [TestCase("Brazzers.Exxtra.20.01.02.Some.Title.1080p.mp4")]
        [TestCase("Some Movie 2020 1080p.mkv")]
        [TestCase("x264.mkv")]
        [TestCase("1080p.mp4")]
        [TestCase("Blacked - Some Title.mp4")]
        [TestCase(null)]
        public void should_not_extract_code(string title)
        {
            JavCode.TryExtract(title, out var code).Should().BeFalse();
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
