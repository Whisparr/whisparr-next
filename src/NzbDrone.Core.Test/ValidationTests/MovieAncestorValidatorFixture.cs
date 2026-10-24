using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ValidationTests
{
    [TestFixture]
    public class MovieAncestorValidatorFixture : CoreTest<MovieAncestorValidator<Movie>>
    {
        private TestValidator<Movie> _validator;

        [SetUp]
        public void Setup()
        {
            PosixOnly();

            _validator = new TestValidator<Movie>
                            {
                                v => v.RuleFor(s => s.Path).SetValidator(Subject)
                            };
        }

        private void GivenExisting(ItemType itemType, string path)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePathEntries())
                  .Returns(new List<MoviePathEntry>
                           {
                               new() { Id = 100, Path = path, ItemType = itemType }
                           });
        }

        private static Movie NewItem(ItemType itemType, string path)
        {
            var movie = new Movie { Id = 1, Path = path };
            movie.MovieMetadata.Value.ItemType = itemType;
            return movie;
        }

        [Test]
        public void scene_path_that_is_an_ancestor_of_another_scene_is_valid()
        {
            GivenExisting(ItemType.Scene, "/lib/scenes/Shady Spa/Scene A");

            _validator.Validate(NewItem(ItemType.Scene, "/lib/scenes/Shady Spa")).IsValid.Should().BeTrue();
        }

        [Test]
        public void scene_path_that_is_an_ancestor_of_a_movie_is_valid()
        {
            GivenExisting(ItemType.Movie, "/lib/scenes/Shady Spa/Some Movie");

            _validator.Validate(NewItem(ItemType.Scene, "/lib/scenes/Shady Spa")).IsValid.Should().BeTrue();
        }

        [Test]
        public void scene_added_after_movie_nested_in_studio_folder_is_valid()
        {
            GivenExisting(ItemType.Movie, "/lib/scenes/Shady Spa/Some Movie");

            _validator.Validate(NewItem(ItemType.Scene, "/lib/scenes/Shady Spa")).IsValid.Should().BeTrue();
        }

        [TestCase(ItemType.Scene)]
        [TestCase(ItemType.Movie)]
        public void movie_path_that_is_an_ancestor_of_anything_is_invalid(ItemType existing)
        {
            GivenExisting(existing, "/lib/movies/Some Movie/Extras");

            _validator.Validate(NewItem(ItemType.Movie, "/lib/movies/Some Movie")).IsValid.Should().BeFalse();
        }

        [Test]
        public void movie_path_that_is_not_an_ancestor_is_valid()
        {
            GivenExisting(ItemType.Movie, "/lib/movies/Other Movie");

            _validator.Validate(NewItem(ItemType.Movie, "/lib/movies/Some Movie")).IsValid.Should().BeTrue();
        }
    }
}
