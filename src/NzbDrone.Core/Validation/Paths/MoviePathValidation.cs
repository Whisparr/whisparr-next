using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Validation.Paths
{
    public class MoviePathValidator<T> : PropertyValidator<T, string>
    {
        private readonly IMovieService _moviesService;

        public MoviePathValidator(IMovieService moviesService)
        {
            _moviesService = moviesService;
        }

        public override string Name => "MoviePathValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is already configured for an existing movie";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            dynamic instance = context.InstanceToValidate;
            var instanceId = (int)instance.Id;

            context.MessageFormatter.AppendArgument("path", value);

            var instanceUsesSceneLayout = ValidatedItemType.UsesSceneLayout(instance);

            // Scene and JAV items may share a path with each other, so they only need checking against other items.
            // This runs for every item added, so loading every scene here makes bulk adds slower as the library grows.
            var existing = instanceUsesSceneLayout ? _moviesService.NonScenePathEntries() : _moviesService.AllMoviePathEntries();

            // Skip the path for this movie and any invalid paths.
            return !existing.Any(s => s.Id != instanceId &&
                                      s.Path.IsPathValid(PathValidationType.CurrentOs) &&
                                      s.Path.PathEquals(value));
        }
    }
}
