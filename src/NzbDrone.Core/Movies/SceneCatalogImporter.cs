using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.ImportExclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies.Performers;

namespace NzbDrone.Core.Movies
{
    public interface ISceneCatalogImporter
    {
        // ownerTitle is for logs only. buildStub(stashId) returns the unsaved Movie to add (ForeignId = stashId plus owner settings).
        // Returns the number of scenes added.
        int ImportScenes(string ownerTitle, Func<string, ScenePage> fetchPage, Func<string, Movie> buildStub);
    }

    public class SceneCatalogImporter : ISceneCatalogImporter
    {
        private readonly IScenePageWalker _scenePageWalker;
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IAddMovieService _addMovieService;
        private readonly IRefreshNewScenes _refreshNewScenes;
        private readonly IMovieService _movieService;
        private readonly IPerformerService _performerService;
        private readonly IImportListExclusionService _exclusionService;
        private readonly Logger _logger;

        public SceneCatalogImporter(IScenePageWalker scenePageWalker,
                                    IProvideMovieInfo movieInfo,
                                    IAddMovieService addMovieService,
                                    IRefreshNewScenes refreshNewScenes,
                                    IMovieService movieService,
                                    IPerformerService performerService,
                                    IImportListExclusionService exclusionService,
                                    Logger logger)
        {
            _scenePageWalker = scenePageWalker;
            _movieInfo = movieInfo;
            _addMovieService = addMovieService;
            _refreshNewScenes = refreshNewScenes;
            _movieService = movieService;
            _performerService = performerService;
            _exclusionService = exclusionService;
            _logger = logger;
        }

        public int ImportScenes(string ownerTitle, Func<string, ScenePage> fetchPage, Func<string, Movie> buildStub)
        {
            var library = new HashSet<string>(_movieService.AllMovieStashIds());
            var excluded = new HashSet<string>(_exclusionService.GetAllExclusions().Select(e => e.ForeignId));
            var addedCount = 0;
            var total = 0;

            var result = _scenePageWalker.Walk(fetchPage, page =>
            {
                total = page.Total;

                var newItems = page.Items
                    .Where(i => !library.Contains(i.StashId) && !excluded.Contains(i.StashId))
                    .GroupBy(i => i.StashId)
                    .Select(g => g.First())
                    .ToList();

                if (newItems.Count == 0)
                {
                    return;
                }

                var knownPerformers = new HashSet<string>(_performerService.AllPerformerForeignIds());
                var unknownPerformers = newItems
                    .SelectMany(i => i.PerformerStashIds ?? new List<string>())
                    .Where(id => !knownPerformers.Contains(id))
                    .Distinct()
                    .ToList();

                if (unknownPerformers.Count > 0)
                {
                    _movieInfo.PrefetchPerformers(unknownPerformers);
                }

                var added = _addMovieService.AddMovies(newItems.Select(i => buildStub(i.StashId)).ToList(), true, refreshInline: true);

                foreach (var movie in added)
                {
                    library.Add(movie.ForeignId);
                }

                addedCount += added.Count;
                _refreshNewScenes.RefreshNewScenes(added);
            });

            _logger.Info("Synced {0}: {1} scenes on Skyhook, added {2}", ownerTitle, total, addedCount);

            if (result.GaveUp.Count > 0)
            {
                _logger.Warn("Gave up on {0} scene(s) for {1}, they will be retried on the next sync: {2}", result.GaveUp.Count, ownerTitle, string.Join(", ", result.GaveUp));
            }

            return addedCount;
        }
    }
}
