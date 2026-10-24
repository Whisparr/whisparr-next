namespace NzbDrone.Core.MediaFiles
{
    public class RenameMovieFilePreview
    {
        public int MovieId { get; set; }
        public int MovieFileId { get; set; }
        public string ExistingPath { get; set; }
        public string NewPath { get; set; }

        // Relative to the root folder instead of the movie folder. Set only when the rename
        // changes the folder, otherwise null. Only the V5 API reads these.
        public string ExistingRootRelativePath { get; set; }
        public string NewRootRelativePath { get; set; }
    }
}
