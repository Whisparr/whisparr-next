using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles.LostFiles
{
    public interface ILostMovieFileRepository : IBasicRepository<LostMovieFile>
    {
        List<LostMovieFile> FindByHash(string osHash, long size);
        void DeleteForMovies(List<int> movieIds);
    }

    public class LostMovieFileRepository : BasicRepository<LostMovieFile>, ILostMovieFileRepository
    {
        public LostMovieFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<LostMovieFile> FindByHash(string osHash, long size)
        {
            return Query(x => x.OsHash == osHash && x.Size == size);
        }

        public void DeleteForMovies(List<int> movieIds)
        {
            Delete(x => movieIds.Contains(x.MovieId));
        }
    }
}
