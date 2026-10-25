using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Config;

namespace NzbDrone.Api.Test.v5.Config
{
    [TestFixture]
    public class HostConfigControllerFixture : TestBase<HostConfigControllerFixture.ValidatingHostConfigController>
    {
        private const string LockedMessage = "Metadata sources are locked";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrMovieMetadataSource)
                  .Returns(MovieMetadataType.TMDB);

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("MetadataSourcesLockedValidation"))
                  .Returns(LockedMessage);
        }

        private List<ValidationFailure> MovieSourceFailures(MovieMetadataType? movieSource)
        {
            var resource = new HostConfigResource { WhisparrMovieMetadataSource = movieSource };

            return Subject.Validator.Validate(resource).Errors
                          .Where(e => e.PropertyName == nameof(HostConfigResource.WhisparrMovieMetadataSource))
                          .ToList();
        }

        [Test]
        public void should_reject_movie_source_change_in_host_config()
        {
            var failures = MovieSourceFailures(MovieMetadataType.TPDB);

            failures.Should().ContainSingle();
            failures.Single().ErrorMessage.Should().Be(LockedMessage);
        }

        [Test]
        public void should_accept_the_stored_movie_source()
        {
            MovieSourceFailures(MovieMetadataType.TMDB).Should().BeEmpty();
        }

        [Test]
        public void should_accept_an_absent_movie_source()
        {
            MovieSourceFailures(null).Should().BeEmpty();
        }

        [Test]
        public void should_return_the_stored_movie_source()
        {
            Subject.GetHostConfig().WhisparrMovieMetadataSource.Should().Be(MovieMetadataType.TMDB);
        }

        // Exposes the shared validator, which RestController only runs inside a live request.
        public class ValidatingHostConfigController : HostConfigController
        {
            public ValidatingHostConfigController(IConfigFileProvider configFileProvider,
                                                  IConfigService configService,
                                                  IUserService userService,
                                                  FileExistsValidator<HostConfigResource> fileExistsValidator,
                                                  OidcAuthorityValidator<HostConfigResource> oidcAuthorityValidator,
                                                  ILocalizationService localizationService)
                : base(configFileProvider, configService, userService, fileExistsValidator, oidcAuthorityValidator, localizationService)
            {
            }

            public IValidator<HostConfigResource> Validator => SharedValidator;
        }
    }
}
