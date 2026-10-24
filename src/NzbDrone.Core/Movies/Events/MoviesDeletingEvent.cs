using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Movies.Events
{
    // Published before the rows are deleted, so a handler can record what the rows own.
    // MovieService can't read files or folder layout itself without a dependency cycle.
    public class MoviesDeletingEvent : IEvent
    {
        public List<Movie> Movies { get; private set; }
        public Dictionary<int, List<string>> OwnedFilePaths { get; private set; }

        public MoviesDeletingEvent(List<Movie> movies)
        {
            Movies = movies;
            OwnedFilePaths = new Dictionary<int, List<string>>();
        }
    }
}
