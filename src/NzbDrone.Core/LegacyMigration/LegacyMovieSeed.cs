using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.LegacyMigration
{
    // A movie from the other metadata source that has no twin in the chosen one. Its file waits as an ImportFiles row
    // and the seed carries what the user had set, until the movie is added from the chosen source.
    public class LegacyMovieSeed : ModelBase
    {
        public string LegacyForeignId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public bool Monitored { get; set; }
        public int QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }

        // JSON, as stored on Movies
        public string Tags { get; set; }
        public int? ImportFileId { get; set; }
        public bool Resolved { get; set; }
        public int Attempts { get; set; }
        public DateTime? LastAttemptAt { get; set; }
    }
}
