using System.Collections.Generic;
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

    public class DetectionResult
    {
        public bool SetupModeRequired { get; set; }
        public List<LegacyInstall> Installs { get; set; } = new();
        public string LegacyConfigPath { get; set; }

        // Legacy databases that exist but could not be inspected. Never null.
        public List<string> Errors { get; set; } = new();
    }
}
