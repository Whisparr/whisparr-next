using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Test.MovieTests.Jav
{
    [TestFixture]
    public class MovieToFormattedStringFixture
    {
        [Test]
        public void should_format_jav_as_code_and_title()
        {
            var movie = new Movie
            {
                MovieMetadata = new MovieMetadata { ItemType = ItemType.Jav, Code = "ABC-123", Title = "Some Title" }
            };

            movie.ToFormattedString().Should().Be("ABC-123 - Some Title");
        }
    }
}
