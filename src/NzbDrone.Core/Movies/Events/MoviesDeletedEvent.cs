using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Movies.Events
{
    public class MoviesDeletedEvent : IEvent
    {
        public List<Movie> Movies { get; private set; }
        public bool DeleteFiles { get; private set; }
        public bool AddImportListExclusion { get; private set; }

        // Full paths of each scene's own video files and extras, keyed by movie id. Captured
        // before the rows are deleted, and only for scenes in shared folders.
        public Dictionary<int, List<string>> OwnedFilePaths { get; private set; }

        public MoviesDeletedEvent(List<Movie> movies, bool deleteFiles, bool addImportListExclusion, Dictionary<int, List<string>> ownedFilePaths = null)
        {
            Movies = movies;
            DeleteFiles = deleteFiles;
            AddImportListExclusion = addImportListExclusion;
            OwnedFilePaths = ownedFilePaths ?? new Dictionary<int, List<string>>();
        }
    }
}
