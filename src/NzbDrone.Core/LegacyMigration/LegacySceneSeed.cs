using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.LegacyMigration
{
    // A scene from the Eros library, with what the user had set and the file it had, until the scene is matched on the
    // new scene source and added there. The File* columns are null or 0 when the scene had no file.
    public class LegacySceneSeed : ModelBase
    {
        public string LegacyForeignId { get; set; }
        public string Title { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string LegacyStudioForeignId { get; set; }
        public bool Monitored { get; set; }
        public int QualityProfileId { get; set; }
        public string Path { get; set; }
        public string RootFolderPath { get; set; }

        // JSON, as stored on the scene
        public string Tags { get; set; }
        public int? ImportFileId { get; set; }
        public long FileSize { get; set; }

        // JSON, as stored on the file
        public string FileQuality { get; set; }
        public string FileLanguages { get; set; }
        public string FileMediaInfo { get; set; }
        public string FileReleaseGroup { get; set; }
        public string FileSceneName { get; set; }
        public string FileOriginalFilePath { get; set; }
        public int FileIndexerFlags { get; set; }
        public string FileEdition { get; set; }
        public DateTime? FileDateAdded { get; set; }
        public bool Resolved { get; set; }
        public int Attempts { get; set; }
        public DateTime? LastAttemptAt { get; set; }
    }
}
