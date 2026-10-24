using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles.LostFiles
{
    // A scene file that went missing from disk. Kept for a while so a file with the same oshash and size, turning up in
    // another library folder (StashApp moved it), can be re-linked to the scene.
    public class LostMovieFile : ModelBase
    {
        public int MovieId { get; set; }
        public string OsHash { get; set; }
        public long Size { get; set; }
        public string RelativePath { get; set; }
        public DateTime LostAt { get; set; }
    }
}
