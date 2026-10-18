using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Movies.Events
{
    public class MoviesImportedEvent : IEvent
    {
        public List<Movie> Movies { get; private set; }

        public bool RefreshInline { get; private set; }

        public MoviesImportedEvent(List<Movie> movies, bool refreshInline = false)
        {
            Movies = movies;
            RefreshInline = refreshInline;
        }
    }
}
