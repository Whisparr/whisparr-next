using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Test.MovieTests.MovieRepositoryTests
{
    // The real path validators over real rows: a v2 studio folder holds scenes and JAV together
    [TestFixture]
    public class SharedFolderPathValidationFixture : DbTest<MovieRepository, Movie>
    {
        private const string StudioFolder = "/media/scenes/Studio";

        private InlineValidator<Movie> _validator;

        [SetUp]
        public void Setup()
        {
            PosixOnly();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePathEntries())
                  .Returns(() => Subject.AllMoviePathEntries());

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.NonScenePathEntries())
                  .Returns(() => Subject.NonScenePathEntries());

            _validator = new InlineValidator<Movie>();
            _validator.RuleFor(m => m.Path)
                      .SetValidator(Mocker.Resolve<MoviePathValidator<Movie>>())
                      .SetValidator(Mocker.Resolve<MovieAncestorValidator<Movie>>());
        }

        [TestCase(ItemType.Scene, ItemType.Jav, true)]
        [TestCase(ItemType.Jav, ItemType.Scene, true)]
        [TestCase(ItemType.Jav, ItemType.Jav, true)]
        [TestCase(ItemType.Movie, ItemType.Jav, false)]
        [TestCase(ItemType.Jav, ItemType.Movie, false)]
        [TestCase(ItemType.Movie, ItemType.Movie, false)]
        public void should_validate_a_new_item_against_a_stored_row_in_the_same_folder(ItemType stored, ItemType incoming, bool valid)
        {
            GivenStored("stored", StudioFolder, stored);

            var movie = new Movie { Path = StudioFolder };
            movie.MovieMetadata.Value.ItemType = incoming;

            _validator.Validate(movie).IsValid.Should().Be(valid);
        }

        [Test]
        public void should_accept_each_jav_page_into_a_folder_that_already_holds_a_scene_and_jav()
        {
            GivenStored("scene", StudioFolder, ItemType.Scene);
            GivenStored("jav-1", StudioFolder, ItemType.Jav);

            var movie = new Movie { Path = StudioFolder };
            movie.MovieMetadata.Value.ItemType = ItemType.Jav;

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }

        private void GivenStored(string foreignId, string path, ItemType itemType)
        {
            var metadata = Db.Insert(Builder<MovieMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.ForeignId = foreignId)
                .With(m => m.ItemType = itemType)
                .BuildNew());

            Subject.Insert(Builder<Movie>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.MovieMetadataId = metadata.Id)
                .With(m => m.Path = path)
                .BuildNew());
        }
    }
}
