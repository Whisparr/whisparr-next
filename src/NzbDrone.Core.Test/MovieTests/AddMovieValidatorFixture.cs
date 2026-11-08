using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class AddMovieValidatorFixture : CoreTest<AddMovieValidator>
    {
        private static Movie GivenJavMovie()
        {
            return new Movie
            {
                ForeignId = "jav-uuid",
                MovieMetadata = new LazyLoaded<MovieMetadata>(new MovieMetadata
                {
                    ItemType = ItemType.Jav,
                    MetadataSource = Core.Movies.MetadataSource.TpdbJav
                })
            };
        }

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("JavRequiresTpdbSceneSource"))
                  .Returns("JAV needs the TPDB scene metadata source");
        }

        [Test]
        public void should_fail_jav_when_unavailable()
        {
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(false);

            var result = Subject.Validate(GivenJavMovie());

            result.Errors.Should().Contain(e => e.ErrorMessage == "JAV needs the TPDB scene metadata source");
        }

        [Test]
        public void should_pass_jav_rule_when_available()
        {
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(true);

            var result = Subject.Validate(GivenJavMovie());

            result.Errors.Should().NotContain(e => e.ErrorMessage == "JAV needs the TPDB scene metadata source");
        }
    }
}
