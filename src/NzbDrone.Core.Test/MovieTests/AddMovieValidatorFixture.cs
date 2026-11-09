using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.RootFolders;
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

        // The path rules run for real here (not a mocked validator): a v2 studio folder holds scenes and JAV together
        [TestCase(ItemType.Scene, ItemType.Jav, true)]
        [TestCase(ItemType.Jav, ItemType.Jav, true)]
        [TestCase(ItemType.Jav, ItemType.Scene, true)]
        [TestCase(ItemType.Movie, ItemType.Jav, false)]
        [TestCase(ItemType.Jav, ItemType.Movie, false)]
        public void should_apply_the_real_path_rules_to_a_shared_studio_folder(ItemType existing, ItemType incoming, bool valid)
        {
            PosixOnly();

            const string studioFolder = "/data/Studio";

            var entries = new List<MoviePathEntry> { new() { Id = 100, Path = studioFolder, ItemType = existing } };

            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(true);
            Mocker.GetMock<IMovieService>().Setup(s => s.AllMoviePathEntries()).Returns(entries);
            Mocker.GetMock<IMovieService>().Setup(s => s.NonScenePathEntries()).Returns(entries.Where(e => !e.ItemType.UsesSceneLayout()).ToList());
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>());

            var movie = new Movie { Path = studioFolder };
            movie.MovieMetadata.Value.ItemType = incoming;
            movie.MovieMetadata.Value.MetadataSource = incoming == ItemType.Jav ? Core.Movies.MetadataSource.TpdbJav : Core.Movies.MetadataSource.TpdbScene;

            Subject.Validate(movie).Errors.Any(e => e.ErrorMessage.Contains("already configured")).Should().Be(!valid);
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
