using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class FileNameValidationFixture : CoreTest
    {
        private const string CollisionMessage = "Must not start with the movie or scene import folder";
        private const string UniqueNamesMessage = "Flat scene folders need {Release Date} or {Scene ForeignId} in the scene file format to keep file names unique";

        private InlineValidator<NamingConfig> _validator;

        [SetUp]
        public void Setup()
        {
            _validator = new InlineValidator<NamingConfig>();
            _validator.RuleFor(c => c.SceneFolderFormat).ValidSceneFolderFormat();
            _validator.RuleFor(c => c.SceneFolderFormat).ValidMainSceneFolderFormat();
            _validator.RuleFor(c => c.SceneFolderFormat).NotCollidingWithMainFolders(c => c.MovieFolderFormat, c => c.SceneImportFolderFormat);
            _validator.RuleFor(c => c.StandardSceneFormat).FlatSceneFolderKeepsFileNamesUnique(c => c.SceneFolderFormat).AsWarning();
        }

        private static NamingConfig Config(string sceneFolderFormat, string standardSceneFormat = null)
        {
            return new NamingConfig
            {
                MovieFolderFormat = "movies/{Studio CleanTitle}/{Movie Title}",
                SceneImportFolderFormat = "import/",
                SceneFolderFormat = sceneFolderFormat,
                StandardSceneFormat = standardSceneFormat ?? NamingConfig.Default.StandardSceneFormat
            };
        }

        [TestCase("scenes/{Studio CleanNetwork}/{Studio CleanTitle}")]
        [TestCase("{Studio Title}")]
        [TestCase("{Studio CleanTitle}/{Release Date} - {Scene CleanTitle}")]
        public void should_accept_flat_and_unprefixed_scene_folder_formats(string format)
        {
            _validator.Validate(Config(format)).Errors.Should().BeEmpty();
        }

        [Test]
        public void should_accept_default_scene_folder_format()
        {
            _validator.Validate(NamingConfig.Default).Errors.Should().BeEmpty();
        }

        [TestCase("movies/{Studio Title}")]
        [TestCase("Movies\\{Studio Title}")]
        [TestCase("import/{Studio Title}")]
        [TestCase("IMPORT/{Studio Title}")]
        public void should_reject_scene_folder_starting_with_movie_or_import_folder(string format)
        {
            var errors = _validator.Validate(Config(format)).Errors;

            errors.Should().ContainSingle().Which.ErrorMessage.Should().Be(CollisionMessage);
        }

        [TestCase("moviesfoo/{Studio Title}")]
        [TestCase("{Studio Title}/movies")]
        [TestCase("{Studio CleanNetwork}/movies/{Studio Title}")]
        public void should_not_treat_other_first_segments_as_collision(string format)
        {
            _validator.Validate(Config(format)).Errors.Should().BeEmpty();
        }

        [Test]
        public void should_still_require_studio_token()
        {
            var errors = _validator.Validate(Config("scenes/{Release Date}")).Errors;

            errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("Must contain scene studio token, ex. {Studio CleanTitle}");
        }

        [Test]
        public void should_reject_empty_scene_folder_format()
        {
            _validator.Validate(Config("")).IsValid.Should().BeFalse();
        }

        [Test]
        public void should_warn_when_flat_folder_and_file_format_may_collide()
        {
            var errors = _validator.Validate(Config("scenes/{Studio CleanTitle}", "{Scene Title}")).Errors;

            var error = errors.Should().ContainSingle().Subject;
            error.ErrorMessage.Should().Be(UniqueNamesMessage);
            error.PropertyName.Should().Be(nameof(NamingConfig.StandardSceneFormat));
            error.CustomState.Should().Be(NzbDroneValidationState.Warning);
        }

        [TestCase("{Release Date} - {Scene Title}")]
        [TestCase("{Release-Date} - {Scene Title}")]
        [TestCase("{Scene Title} {[Scene ForeignId]}")]
        public void should_not_warn_when_file_format_has_date_or_foreign_id(string fileFormat)
        {
            _validator.Validate(Config("scenes/{Studio CleanTitle}", fileFormat)).Errors.Should().BeEmpty();
        }

        [Test]
        public void should_not_warn_when_folder_is_not_flat()
        {
            _validator.Validate(Config("scenes/{Studio CleanTitle}/{Scene CleanTitle}", "{Scene Title}")).Errors.Should().BeEmpty();
        }

        [Test]
        public void should_treat_empty_file_format_as_not_unique()
        {
            // An empty file format is rejected elsewhere (ValidSceneFormat); the warning rule only judges uniqueness.
            var errors = _validator.Validate(Config("scenes/{Studio CleanTitle}", "")).Errors;

            errors.Should().ContainSingle().Which.CustomState.Should().Be(NzbDroneValidationState.Warning);
        }
    }
}
