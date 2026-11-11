using System.Collections.Generic;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles.Events
{
    public class MovieScannedEvent : IEvent
    {
        public Movie Movie { get; private set; }
        public List<string> PossibleExtraFiles { get; set; }

        // A side file's path to its owner's MovieFile.Id, resolved once by the shared folder pass; null outside it
        public Dictionary<string, int> SideFileOwners { get; set; }

        public MovieScannedEvent(Movie movie, List<string> possibleExtraFiles)
        {
            Movie = movie;
            PossibleExtraFiles = possibleExtraFiles;
        }
    }
}
