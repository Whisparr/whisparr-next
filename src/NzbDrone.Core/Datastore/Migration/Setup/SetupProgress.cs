using System.Collections.Generic;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public enum SetupStep
    {
        Backup,
        SeedConfig,
        CreateDatabase,
        Migrate,
        PortSettings,
        SnapshotLibrary,
        MixedMovies,
        SkyhookPreflight,
        LockSources,
        Finish
    }

    public enum SetupStatus
    {
        Idle,
        Running,
        Failed,
        Succeeded,
        Restarting
    }

    public static class SetupErrorKey
    {
        public const string SetupNotAvailableYet = "SetupNotAvailableYet";
        public const string PostgresCreateFailed = "PostgresCreateFailed";
        public const string SkyhookUnavailable = "SkyhookUnavailable";
        public const string BackupFailed = "BackupFailed";
        public const string Unexpected = "Unexpected";
    }

    public static class SetupStepState
    {
        public const string Pending = "pending";
        public const string Running = "running";
        public const string Done = "done";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
    }

    // A snapshot of phase 1, for the setup page to poll.
    public class SetupProgress
    {
        public SetupStatus Status { get; set; }

        // State is one of SetupStepState.
        public List<(SetupStep Step, string State)> Steps { get; set; } = new();

        public string BackupPath { get; set; }
        public string ErrorMessage { get; set; }

        // One of SetupErrorKey.
        public string ErrorKey { get; set; }
        public bool CanRetry { get; set; }

        // The environment variables that still name a legacy database, so v5 can't use its own yet. When there are
        // any the run ends in Succeeded without a restart and the page shows what to change.
        public List<string> EnvVarsToChange { get; set; } = new();

        // The value each variable in EnvVarsToChange needs.
        public List<EnvTarget> EnvTargets { get; set; } = new();

        // Movies outside every root folder that were left untouched, for the page to mention.
        public int SkippedMovies { get; set; }
        public List<string> SkippedMoviePaths { get; set; } = new();
    }
}
