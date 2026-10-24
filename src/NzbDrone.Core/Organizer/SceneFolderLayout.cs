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
    }

    public class SceneFolderLayout : ISceneFolderLayout,
                                     IHandle<MovieAddedEvent>,
                                     IHandle<MoviesImportedEvent>,
                                     IHandle<MoviesDeletedEvent>,
                                     IHandle<MovieEditedEvent>,
                                     IHandle<MovieMovedEvent>,
                                     IHandle<MovieRenamedEvent>
    {
        private static readonly Regex TokenRegex = new(@"\{[^}]+\}", RegexOptions.Compiled, RegexDefaults.Timeout);
        private static readonly char[] PathSeparators = { '/', '\\' };

        private readonly IMovieService _movieService;
        private readonly INamingConfigService _namingConfigService;
        private readonly ICached<List<MoviePathEntry>> _pathCache;

        public SceneFolderLayout(IMovieService movieService,
                                 INamingConfigService namingConfigService,
                                 ICacheManager cacheManager)
        {
            _movieService = movieService;
            _namingConfigService = namingConfigService;
            _pathCache = cacheManager.GetCache<List<MoviePathEntry>>(GetType());
        }

        // A format is flat when its last path segment (the folder the files land in) names
        // no token, or only Studio tokens, so every scene of a studio shares one folder.
        public static bool FormatIsFlat(string sceneFolderFormat)
        {
            var lastSegment = (sceneFolderFormat ?? string.Empty)
                .Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (lastSegment.IsNullOrWhiteSpace())
            {
                return true;
            }

            return TokenRegex.Matches(lastSegment)
                .Select(m => m.Value.Trim('{', '}').TrimStart('[', '('))
                .All(name => name.StartsWith("Studio", StringComparison.Ordinal));
        }

        public bool IsFlatFormat(string sceneFolderFormat)
        {
            return FormatIsFlat(sceneFolderFormat);
        }

        public bool IsFlat()
        {
            return FormatIsFlat(_namingConfigService.GetConfig().SceneFolderFormat);
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

        public void Handle(MovieMovedEvent message)
        {
            _pathCache.Clear();
        }

        public void Handle(MovieRenamedEvent message)
        {
            _pathCache.Clear();
        }

        private List<MoviePathEntry> AllPathEntries()
        {
            return _pathCache.Get("all", () => _movieService.AllMoviePathEntries(), TimeSpan.FromMinutes(5));
        }
    }
}
