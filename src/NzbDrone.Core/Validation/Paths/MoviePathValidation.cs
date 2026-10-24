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

            var instanceIsScene = ValidatedItemType.IsScene(instance);

            // Skip the path for this movie and any invalid paths. Scenes may share a path with other scenes.
            return !_moviesService.AllMoviePathEntries().Any(s => s.Id != instanceId &&
                                                                  !(instanceIsScene && s.ItemType == ItemType.Scene) &&
                                                                  s.Path.IsPathValid(PathValidationType.CurrentOs) &&
                                                                  s.Path.PathEquals(value));
        }
    }
}
