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
    public class MoviePathValidatorFixture : CoreTest<MoviePathValidator<Movie>>
    {
        private const string SharedPath = "/lib/scenes/Shady Spa";

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

        private void GivenExisting(ItemType itemType, string path = SharedPath)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePathEntries())
                  .Returns(new List<MoviePathEntry>
                           {
                               new() { Id = 100, Path = path, ItemType = itemType }
                           });
        }

        private static Movie NewItem(ItemType itemType, string path = SharedPath)
        {
            var movie = new Movie { Id = 1, Path = path };
            movie.MovieMetadata.Value.ItemType = itemType;
            return movie;
        }

        [Test]
        public void scene_may_share_path_with_another_scene()
        {
            GivenExisting(ItemType.Scene);

            _validator.Validate(NewItem(ItemType.Scene)).IsValid.Should().BeTrue();
        }

        [Test]
        public void scene_may_not_share_path_with_a_movie()
        {
            GivenExisting(ItemType.Movie);

            _validator.Validate(NewItem(ItemType.Scene)).IsValid.Should().BeFalse();
        }

        [Test]
        public void movie_may_not_share_path_with_a_scene()
        {
            GivenExisting(ItemType.Scene);

            _validator.Validate(NewItem(ItemType.Movie)).IsValid.Should().BeFalse();
        }

        [Test]
        public void movie_may_not_share_path_with_a_movie()
        {
            GivenExisting(ItemType.Movie);

            _validator.Validate(NewItem(ItemType.Movie)).IsValid.Should().BeFalse();
        }

        [Test]
        public void item_may_keep_its_own_path()
        {
            GivenExisting(ItemType.Movie);

            var movie = NewItem(ItemType.Movie);
            movie.Id = 100;

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }

        [Test]
        public void instance_without_item_type_is_treated_as_a_movie()
        {
            GivenExisting(ItemType.Scene);

            var validator = new TestValidator<NoItemTypeInstance>
                            {
                                v => v.RuleFor(s => s.Path).SetValidator(new MoviePathValidator<NoItemTypeInstance>(Mocker.Resolve<IMovieService>()))
                            };

            validator.Validate(new NoItemTypeInstance { Id = 1, Path = SharedPath }).IsValid.Should().BeFalse();
        }

        [Test]
        public void resource_with_scene_item_type_may_share_path_with_a_scene()
        {
            GivenExisting(ItemType.Scene);

            var validator = new TestValidator<ResourceLikeInstance>
                            {
                                v => v.RuleFor(s => s.Path).SetValidator(new MoviePathValidator<ResourceLikeInstance>(Mocker.Resolve<IMovieService>()))
                            };

            validator.Validate(new ResourceLikeInstance { Id = 1, Path = SharedPath, ItemType = ItemType.Scene }).IsValid.Should().BeTrue();
        }

        public class NoItemTypeInstance
        {
            public int Id { get; set; }
            public string Path { get; set; }
        }

        public class ResourceLikeInstance
        {
            public int Id { get; set; }
            public string Path { get; set; }
            public ItemType ItemType { get; set; }
        }
    }
}
