using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;

namespace Whisparr.Http.Setup
{
    public class SetupInstallResource
    {
        public LegacyKind Kind { get; set; }
        public DatabaseType DatabaseType { get; set; }
        public LegacyRefusal Refusal { get; set; }
        public int Studios { get; set; }
        public int Scenes { get; set; }
        public int Movies { get; set; }
        public int Files { get; set; }
        public SceneMetadataType PreviousSceneSource { get; set; }
        public MovieMetadataType PreviousMovieSource { get; set; }
        public bool HasMixedMovies { get; set; }
        public bool NamedByEnvironment { get; set; }

        public static SetupInstallResource MapToResource(LegacyInstall install)
        {
            return new SetupInstallResource
            {
                Kind = install.Kind,
                DatabaseType = install.Database?.Type ?? DatabaseType.SQLite,
                Refusal = install.Refusal,
                Studios = install.Studios,
                Scenes = install.Scenes,
                Movies = install.Movies,
                Files = install.Files,
                PreviousSceneSource = install.PreviousSceneSource,
                PreviousMovieSource = install.PreviousMovieSource,
                HasMixedMovies = install.HasMixedMovies,
                NamedByEnvironment = install.Database?.NamedByEnvironment ?? false
            };
        }
    }

    public class SetupEnvTargetResource
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public static SetupEnvTargetResource MapToResource(EnvTarget target)
        {
            return new SetupEnvTargetResource { Name = target.Name, Value = target.Value };
        }
    }

    public class SetupDefaultsResource
    {
        public LegacyKind Kind { get; set; }
        public SceneMetadataType SceneSource { get; set; }
        public MovieMetadataType MovieSource { get; set; }
    }

    public class SetupStepResource
    {
        public SetupStep Step { get; set; }

        // One of pending, running, done, failed, skipped.
        public string State { get; set; }
    }

    public class SetupProgressResource
    {
        public SetupStatus Status { get; set; }
        public List<SetupStepResource> Steps { get; set; } = new();
        public string BackupPath { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorKey { get; set; }
        public bool CanRetry { get; set; }
        public List<string> EnvVarsToChange { get; set; } = new();
        public List<SetupEnvTargetResource> EnvTargets { get; set; } = new();
        public int SkippedMovies { get; set; }
        public List<string> SkippedMoviePaths { get; set; } = new();

        public static SetupProgressResource MapToResource(SetupProgress progress)
        {
            return new SetupProgressResource
            {
                Status = progress.Status,
                Steps = progress.Steps.Select(s => new SetupStepResource { Step = s.Step, State = s.State }).ToList(),
                BackupPath = progress.BackupPath,
                ErrorMessage = progress.ErrorMessage,
                ErrorKey = progress.ErrorKey,
                CanRetry = progress.CanRetry,
                EnvVarsToChange = progress.EnvVarsToChange,
                EnvTargets = progress.EnvTargets.Select(SetupEnvTargetResource.MapToResource).ToList(),
                SkippedMovies = progress.SkippedMovies,
                SkippedMoviePaths = progress.SkippedMoviePaths
            };
        }
    }

    public class SetupStatusResource
    {
        public List<SetupInstallResource> Installs { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public bool PendingEnvSwitch { get; set; }

        // False when there is no one to sign in as, so the page is only served to local addresses.
        public bool HasUsableCredentials { get; set; }
        public List<SetupEnvTargetResource> EnvTargets { get; set; } = new();
        public SetupDefaultsResource Defaults { get; set; }
        public SetupProgressResource Progress { get; set; }
    }
}
