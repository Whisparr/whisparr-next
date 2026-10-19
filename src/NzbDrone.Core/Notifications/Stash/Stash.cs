using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications.Stash
{
    public class Stash : NotificationBase<StashSettings>
    {
        private readonly IStashService _stashService;
        private readonly IConfigService _configService;

        public Stash(IStashService stashService, IConfigService configService)
        {
            _stashService = stashService;
            _configService = configService;
        }

        public override string Link => "https://stashapp.cc/";
        public override string Name => "Stash";

        // A new definition points Identify at the stash-box that matches the locked scene source.
        public override IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                var settings = new StashSettings();

                if (_configService.WhisparrSceneMetadataSource == SceneMetadataType.Tpdb)
                {
                    settings.StashBoxEndpoint = StashSettings.TpdbStashBoxEndpoint;
                }

                yield return new NotificationDefinition
                {
                    Implementation = GetType().Name,
                    Settings = settings
                };
            }
        }

        public override void OnDownload(DownloadMessage message)
        {
            _stashService.Update(Settings, message.Movie);
        }

        public override void OnMovieRename(Movie movie, List<RenamedMovieFile> renamedFiles)
        {
            _stashService.Update(Settings, movie);
            _stashService.Clean(Settings, movie);
        }

        public override void OnMovieFileDelete(MovieFileDeleteMessage deleteMessage)
        {
            _stashService.Clean(Settings, deleteMessage.Movie);
        }

        public override void OnMovieDelete(MovieDeleteMessage deleteMessage)
        {
            _stashService.Clean(Settings, deleteMessage.Movie);
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            failures.AddIfNotNull(_stashService.Test(Settings));

            return new ValidationResult(failures);
        }
    }
}
