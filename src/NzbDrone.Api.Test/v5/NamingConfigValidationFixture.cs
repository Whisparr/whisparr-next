using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Validation;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Config;
using Whisparr.Http.REST;

namespace NzbDrone.Api.Test.v5
{
    [TestFixture]
    public class NamingConfigValidationFixture : TestBase
    {
        private ResourceValidator<NamingConfigResource> _validator;

        [SetUp]
        public void Setup()
        {
            _validator = Mocker.Resolve<ExposedController>().Validator;
        }

        private static NamingConfigResource Resource(string sceneFolderFormat, string standardSceneFormat = null)
        {
            var defaults = NamingConfig.Default;

            return new NamingConfigResource
            {
                StandardMovieFormat = defaults.StandardMovieFormat,
                MovieFolderFormat = defaults.MovieFolderFormat,
                StandardSceneFormat = standardSceneFormat ?? defaults.StandardSceneFormat,
                SceneFolderFormat = sceneFolderFormat,
                SceneImportFolderFormat = defaults.SceneImportFolderFormat
            };
        }

        [TestCase("scenes/{Studio CleanNetwork}/{Studio CleanTitle}")]
        [TestCase("{Studio Title}")]
        public void should_accept_flat_scene_folder_format(string format)
        {
            _validator.Validate(Resource(format)).Errors.Should().BeEmpty();
        }

        [Test]
        public void should_accept_default_naming_config()
        {
            _validator.Validate(Resource(NamingConfig.Default.SceneFolderFormat)).Errors.Should().BeEmpty();
        }

        [TestCase("movies/{Studio Title}")]
        [TestCase("import/{Studio Title}")]
        public void should_reject_scene_folder_colliding_with_main_folders(string format)
        {
            _validator.Validate(Resource(format)).Errors.Should().ContainSingle()
                .Which.ErrorMessage.Should().Be("Must not start with the movie or scene import folder");
        }

        [Test]
        public void should_warn_when_flat_folder_file_format_may_collide()
        {
            _validator.Validate(Resource("scenes/{Studio CleanTitle}", "{Scene Title}")).Errors.Should().ContainSingle()
                .Which.CustomState.Should().Be(NzbDroneValidationState.Warning);
        }

        private class ExposedController : NamingConfigController
        {
            public ExposedController(INamingConfigService namingConfigService, IFilenameSampleService filenameSampleService, IFilenameValidationService filenameValidationService)
                : base(namingConfigService, filenameSampleService, filenameValidationService)
            {
            }

            public ResourceValidator<NamingConfigResource> Validator => SharedValidator;
        }
    }
}
