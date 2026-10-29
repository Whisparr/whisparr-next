using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public enum LegacyKind
    {
        V2,
        Eros
    }

    public enum LegacyRefusal
    {
        None,
        V2TooOld,
        ErosNewerThanV5
    }

    public class LegacyDatabaseLocation
    {
        public DatabaseType Type { get; set; }
        public string SqlitePath { get; set; }
        public string PostgresMainDb { get; set; }

        // The legacy main database connection string, used read-only.
        public string PostgresConnectionString { get; set; }

        // True when Whisparr__Postgres__MainDb or MainDbConnectionString named the database.
        public bool NamedByEnvironment { get; set; }
    }

    public class LegacyInstall
    {
        public LegacyKind Kind { get; set; }
        public LegacyDatabaseLocation Database { get; set; }
        public LegacyRefusal Refusal { get; set; }
        public long MaxMigration { get; set; }
        public int Studios { get; set; }
        public int Scenes { get; set; }
        public int Movies { get; set; }
        public int Files { get; set; }
        public SceneMetadataType PreviousSceneSource { get; set; }
        public MovieMetadataType PreviousMovieSource { get; set; }
        public bool HasUsableCredentials { get; set; }

        // Eros only.
        public bool HasMixedMovies { get; set; }
    }

    // An environment variable and the value it needs for v5 to use its own database.
    public class EnvTarget
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class DetectionResult
    {
        public bool SetupModeRequired { get; set; }

        // The v5 database is already built and marked, but an environment variable still names the legacy one. The
        // migration is not offered again, only the instructions to switch the variable. Installs stays empty.
        public bool PendingEnvSwitch { get; set; }
        public List<EnvTarget> EnvTargets { get; set; } = new();
        public List<LegacyInstall> Installs { get; set; } = new();
        public string LegacyConfigPath { get; set; }

        // Where the sign-in users live when Installs is empty. For a pending env switch that is the v5 main
        // database, which holds the Users rows the migration copied.
        public LegacyDatabaseLocation UserSource { get; set; }

        // For a pending env switch: whether that database has a user (or OIDC) to sign in with.
        public bool PendingSwitchHasUsableCredentials { get; set; }

        // Whether somebody off the local network could sign in to the setup page.
        public bool HasUsableCredentials => PendingEnvSwitch ? PendingSwitchHasUsableCredentials : Installs.Any(i => i.HasUsableCredentials);

        // Legacy databases that exist but could not be inspected. Never null.
        public List<string> Errors { get; set; } = new();
    }
}
