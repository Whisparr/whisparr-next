using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Movies.Studios
{
    public interface IStudioRepository : IBasicRepository<Studio>
    {
        Studio FindByForeignId(string foreignId);
        List<Studio> FindByForeignIds(List<string> foreignIds);
        List<Studio> FindByIds(List<int> ids);
        List<Studio> SearchStudios(string cleanTitle, string foreignId);
        Dictionary<string, List<int>> AllCleanTitles();
        List<string> AllStudioForeignIds();
        List<int> AllStudioIdsByLastInfoSync();
    }

    public class StudioRepository : BasicRepository<Studio>, IStudioRepository
    {
        public StudioRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        // Unlike Get(ids), doesn't throw when a studio was deleted after its id was looked up
        public List<Studio> FindByIds(List<int> ids)
        {
            return Query(x => ids.Contains(x.Id)).ToList();
        }

        public List<Studio> SearchStudios(string cleanTitle, string foreignId)
        {
            return Query(x => x.CleanTitle.Contains(cleanTitle) || x.ForeignId == foreignId).ToList();
        }

        /// <summary> Every title a studio can be found by: its clean title, clean search title and cleaned aliases, mapped to the ids of the studios that use it. </summary>
        public Dictionary<string, List<int>> AllCleanTitles()
        {
            var lookup = new Dictionary<string, List<int>>();

            foreach (var studio in All())
            {
                // CleanSearchTitle is only ever written by the API's resource mapper, and stores
                // string.Empty when the studio has no search title. Studios created by the
                // metadata and scan paths leave it null. A blank key would match every studio
                // the user has saved through the UI, so blanks are left out.
                var titles = new[] { studio.CleanTitle, studio.CleanSearchTitle }
                    .Concat(studio.Aliases?.Select(alias => alias.CleanStudioTitle()?.ToLower()) ?? Enumerable.Empty<string>())
                    .Where(t => t.IsNotNullOrWhiteSpace())
                    .Distinct();

                foreach (var title in titles)
                {
                    if (!lookup.TryGetValue(title, out var ids))
                    {
                        lookup[title] = ids = new List<int>();
                    }

                    ids.Add(studio.Id);
                }
            }

            return lookup;
        }

        public Studio FindByForeignId(string foreignId)
        {
            return Query(x => x.ForeignId == foreignId).FirstOrDefault();
        }

        public List<Studio> FindByForeignIds(List<string> foreignIds)
        {
            return Query(x => foreignIds.Contains(x.ForeignId)).ToList();
        }

        public List<string> AllStudioForeignIds()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<string>("SELECT \"ForeignId\" FROM \"Studios\"").ToList();
            }
        }

        public List<int> AllStudioIdsByLastInfoSync()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<int>("SELECT \"Id\" FROM \"Studios\" ORDER BY \"LastInfoSync\" ASC").ToList();
            }
        }

        public override PagingSpec<Studio> GetPaged(PagingSpec<Studio> pagingSpec)
        {
            return base.GetPaged(pagingSpec);
        }
    }
}
