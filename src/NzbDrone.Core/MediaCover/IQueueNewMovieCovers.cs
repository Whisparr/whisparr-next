using System.Collections.Generic;

namespace NzbDrone.Core.MediaCover
{
    public interface IQueueNewMovieCovers
    {
        void QueueNewMovieCovers(IEnumerable<int> movieIds);
    }
}
