using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace Whisparr.Api.V5.Performers
{
    public class PerformerEditorValidator : AbstractValidator<Performer>
    {
        public PerformerEditorValidator(RootFolderExistsValidator<Performer> rootFolderExistsValidator, QualityProfileExistsValidator<Performer> qualityProfileExistsValidator, IJavAvailability javAvailability, ILocalizationService localizationService)
        {
            RuleFor(s => s.JavMonitored)
                .Must(monitored => !monitored || javAvailability.IsAvailable())
                .WithMessage(_ => localizationService.GetLocalizedString("JavRequiresTpdbSceneSource"));

            RuleFor(s => s.RootFolderPath).Cascade(CascadeMode.Stop)
                .IsValidPath()
                .SetValidator(rootFolderExistsValidator)
                .When(s => s.RootFolderPath.IsNotNullOrWhiteSpace());

            RuleFor(s => s.QualityProfileId).Cascade(CascadeMode.Stop)
                .ValidId()
                .SetValidator(qualityProfileExistsValidator);
        }
    }
}
