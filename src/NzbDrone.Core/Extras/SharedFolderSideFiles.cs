using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Extras
{
    // FilesByMovie: each item's tracked side files still in the listing plus the new ones it owns.
    // OwnerFileIds: a new side file's path to its owner's MovieFile.Id.
    public record SideFileAssignment(Dictionary<int, List<string>> FilesByMovie, Dictionary<string, int> OwnerFileIds);

    public interface ISharedFolderSideFiles
    {
        // Resolves the owner of every side file in a shared folder once, for all of the folder's items
        SideFileAssignment Assign(string folder, IReadOnlyCollection<int> itemIds, List<string> sideFiles, List<MovieFile> folderVideoFiles);
    }

    public class SharedFolderSideFiles : ISharedFolderSideFiles
    {
        private readonly IExtraFileService<OtherExtraFile> _otherExtraFileService;
        private readonly IExtraFileService<MetadataFile> _metadataFileService;
        private readonly IExtraFileService<SubtitleFile> _subtitleFileService;
        private readonly IMovieService _movieService;
        private readonly IDiskProvider _diskProvider;
        private readonly ICached<string> _unidentifiedCache;
        private readonly Logger _logger;

        public SharedFolderSideFiles(IExtraFileService<OtherExtraFile> otherExtraFileService,
                                     IExtraFileService<MetadataFile> metadataFileService,
                                     IExtraFileService<SubtitleFile> subtitleFileService,
                                     IMovieService movieService,
                                     IDiskProvider diskProvider,
                                     ICacheManager cacheManager,
                                     Logger logger)
        {
            _otherExtraFileService = otherExtraFileService;
            _metadataFileService = metadataFileService;
            _subtitleFileService = subtitleFileService;
            _movieService = movieService;
            _diskProvider = diskProvider;
            _unidentifiedCache = cacheManager.GetCache<string>(GetType(), "unidentified");
            _logger = logger;
        }

        public SideFileAssignment Assign(string folder, IReadOnlyCollection<int> itemIds, List<string> sideFiles, List<MovieFile> folderVideoFiles)
        {
            var filesByMovie = itemIds.Distinct().ToDictionary(id => id, _ => new List<string>());
            var owners = new Dictionary<string, int>(PathEqualityComparer.Instance);
            var listed = sideFiles.ToHashSet(PathEqualityComparer.Instance);
            var tracked = new HashSet<string>(PathEqualityComparer.Instance);

            // A file any item in the folder tracks stays with its tracker and is never parsed
            var rows = _otherExtraFileService.GetFilesByMovies(itemIds).Cast<ExtraFile>()
                                             .Concat(_metadataFileService.GetFilesByMovies(itemIds))
                                             .Concat(_subtitleFileService.GetFilesByMovies(itemIds));

            foreach (var row in rows)
            {
                var path = Path.Combine(folder, row.RelativePath);

                if (listed.Contains(path))
                {
                    tracked.Add(path);
                    AddTo(filesByMovie, row.MovieId, path);
                }
            }

            // By base name: a flat root's untracked side files would otherwise each be tested against every video
            var videosByBaseName = folderVideoFiles.Where(f => f.RelativePath.IsNotNullOrWhiteSpace())
                                                   .Select(f => (File: f, BaseName: Path.GetFileNameWithoutExtension(f.RelativePath)))
                                                   .ToLookup(v => v.BaseName, StringComparer.OrdinalIgnoreCase);

            foreach (var file in sideFiles.Where(f => !tracked.Contains(f)))
            {
                var matches = SidecarName.MatchingBaseNames(file).SelectMany(b => videosByBaseName[b]).ToList();
                var owner = matches.Count > 0 ? SidecarName.LongestMatch(matches) : FindByParse(file, itemIds, folderVideoFiles);

                if (owner != null)
                {
                    owners[file] = owner.Id;
                    AddTo(filesByMovie, owner.MovieId, file);
                }
            }

            return new SideFileAssignment(filesByMovie, owners);
        }

        private MovieFile FindByParse(string file, IReadOnlyCollection<int> itemIds, List<MovieFile> folderVideoFiles)
        {
            var key = SharedFolderScanner.CacheKey(_diskProvider, _logger, file);

            if (_unidentifiedCache.Find(key) != null)
            {
                return null;
            }

            Movie scene = null;

            try
            {
                // Only the file name: the folder name of a shared folder would resolve generic names to a scene
                var parsed = Parser.Parser.ParseMovieTitle(Path.GetFileName(file), true);
                scene = parsed == null ? null : _movieService.FindScene(parsed);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to identify side file {0} by name", file);
            }

            if (scene == null)
            {
                _unidentifiedCache.Set(key, "1", SharedFolderScanner.UnidentifiedLifetime);
                return null;
            }

            return itemIds.Contains(scene.Id) ? folderVideoFiles.FirstOrDefault(f => f.MovieId == scene.Id) : null;
        }

        // Every row and video comes from itemIds, so movieId always has a list
        private static void AddTo(Dictionary<int, List<string>> filesByMovie, int movieId, string path)
        {
            var files = filesByMovie[movieId];

            if (!files.Contains(path, PathEqualityComparer.Instance))
            {
                files.Add(path);
            }
        }
    }
}
