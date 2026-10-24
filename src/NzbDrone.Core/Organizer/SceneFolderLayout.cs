using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;

namespace NzbDrone.Core.Organizer
{
    public interface ISceneFolderLayout
    {
        bool IsFlatFormat(string sceneFolderFormat);
        bool IsFlat();
        bool IsShared(Movie movie);
        List<MoviePathEntry> GetItemsInFolder(string folder);

        // Items whose folder is, sits inside, or contains the given folder. Read from the cached path index.
        List<MoviePathEntry> GetItemsTouchingFolder(string folder);

        // True when another item's folder is, or sits inside, the given folder.
        bool HoldsOtherItems(int movieId, string folder);
    }

    public class SceneFolderLayout : ISceneFolderLayout,
                                     IHandle<MovieAddedEvent>,
                                     IHandle<MoviesImportedEvent>,
                                     IHandle<MoviesDeletedEvent>,
                                     IHandle<MovieEditedEvent>,
                                     IHandle<MoviesBulkEditedEvent>,
                                     IHandle<MovieMovedEvent>,
                                     IHandle<MovieRenamedEvent>,
                                     IHandle<MovieFileAddedEvent>,
                                     IHandle<NamingConfigSavedEvent>
    {
        private static readonly Regex TokenRegex = new(@"\{[^}]+\}", RegexOptions.Compiled, RegexDefaults.Timeout);
        private static readonly char[] PathSeparators = { '/', '\\' };
        private static readonly string[] SceneLevelPrefixes = { "Scene ", "Scene.", "Scene-", "Scene_", "Release ", "Release.", "Release-", "Release_", "Movie ", "Movie.", "Movie-", "Movie_", "Original ", "Original.", "Original-", "Original_" };
        private static readonly string[] SceneLevelIds = { "StashId", "TmdbId", "ImdbId" };

        // The repository, not the service: MediaFileService depends on this class, and the service reaches it again
        private readonly IMovieRepository _movieRepository;
        private readonly INamingConfigService _namingConfigService;
        private readonly ICached<List<MoviePathEntry>> _pathCache;
        private readonly ICached<bool> _flatCache;

        public SceneFolderLayout(IMovieRepository movieRepository,
                                 INamingConfigService namingConfigService,
                                 ICacheManager cacheManager)
        {
            _movieRepository = movieRepository;
            _namingConfigService = namingConfigService;
            _pathCache = cacheManager.GetCache<List<MoviePathEntry>>(GetType());
            _flatCache = cacheManager.GetCache<bool>(GetType(), "flat");
        }

        // A format is flat unless its last path segment (the folder the files land in) names a
        // scene-identifying token. Anything else, including unrecognised tokens, is the safe
        // "shared" default for deletes and moves.
        public static bool FormatIsFlat(string sceneFolderFormat)
        {
            var lastSegment = (sceneFolderFormat ?? string.Empty)
                .Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (lastSegment.IsNullOrWhiteSpace())
            {
                return true;
            }

            return !TokenRegex.Matches(lastSegment)
                .Select(m => m.Value.Trim('{', '}').TrimStart('[', '('))
                .Any(IsSceneIdentifyingToken);
        }

        private static bool IsSceneIdentifyingToken(string name)
        {
            return SceneLevelPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                   SceneLevelIds.Any(i => name.StartsWith(i, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsFlatFormat(string sceneFolderFormat)
        {
            return FormatIsFlat(sceneFolderFormat);
        }

        public bool IsFlat()
        {
            return _flatCache.Get("flat", () => FormatIsFlat(_namingConfigService.GetConfig().SceneFolderFormat), TimeSpan.FromMinutes(5));
        }

        public bool IsShared(Movie movie)
        {
            if (movie.MovieMetadata.Value.ItemType != ItemType.Scene)
            {
                return false;
            }

            if (IsFlat())
            {
                return true;
            }

            if (movie.Path.IsNullOrWhiteSpace())
            {
                return false;
            }

            return AllPathEntries().Any(e => e.Id != movie.Id &&
                                             e.Path.IsNotNullOrWhiteSpace() &&
                                             (e.Path.PathEquals(movie.Path) || movie.Path.IsParentPath(e.Path)));
        }

        public List<MoviePathEntry> GetItemsInFolder(string folder)
        {
            return AllPathEntries()
                .Where(e => e.Path.IsNotNullOrWhiteSpace() && e.Path.PathEquals(folder))
                .ToList();
        }

        public List<MoviePathEntry> GetItemsTouchingFolder(string folder)
        {
            return AllPathEntries()
                .Where(e => e.Path.IsNotNullOrWhiteSpace() &&
                            (e.Path.PathEquals(folder) || folder.IsParentPath(e.Path) || e.Path.IsParentPath(folder)))
                .ToList();
        }

        public bool HoldsOtherItems(int movieId, string folder)
        {
            return AllPathEntries().Any(e => e.Id != movieId &&
                                             e.Path.IsNotNullOrWhiteSpace() &&
                                             (e.Path.PathEquals(folder) || folder.IsParentPath(e.Path)));
        }

        public void Handle(MovieAddedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(MoviesImportedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(MoviesDeletedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(MovieEditedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(MoviesBulkEditedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(MovieMovedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(MovieRenamedEvent message)
        {
            _pathCache.Clear();
        }

        // An import can recompute Movie.Path while saving the new file without any edit event.
        public void Handle(MovieFileAddedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(NamingConfigSavedEvent message)
        {
            _flatCache.Clear();
        }

        private List<MoviePathEntry> AllPathEntries()
        {
            return _pathCache.Get("all", () => _movieRepository.AllMoviePathEntries(), TimeSpan.FromMinutes(5));
        }
    }
}
