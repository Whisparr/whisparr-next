using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MediaFiles
{
    public interface ISharedFolderScanner
    {
        // Import decisions for the files in the folder that identify as an in-library scene whose Path is movie.Path,
        // siblings included. Everything else is dropped: nothing is searched online, moved, added or recorded as unmapped.
        List<ImportDecision> IdentifyOwned(Movie movie, List<string> unmappedVideoFiles);
    }

    public class SharedFolderScanner : ISharedFolderScanner
    {
        private static readonly TimeSpan UnidentifiedLifetime = TimeSpan.FromMinutes(10);

        private readonly ISceneFolderLayout _sceneFolderLayout;
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IParsingService _parsingService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IFileMatchEvaluator _fileMatchEvaluator;
        private readonly IFingerprintLookupClient _fingerprintLookupClient;
        private readonly IVideoHashService _videoHashService;
        private readonly IDiskProvider _diskProvider;
        private readonly ICached<string> _unidentifiedCache;
        private readonly Logger _logger;

        public SharedFolderScanner(ISceneFolderLayout sceneFolderLayout,
                                   IMovieService movieService,
                                   IMediaFileService mediaFileService,
                                   IParsingService parsingService,
                                   IMakeImportDecision importDecisionMaker,
                                   IFileMatchEvaluator fileMatchEvaluator,
                                   IFingerprintLookupClient fingerprintLookupClient,
                                   IVideoHashService videoHashService,
                                   IDiskProvider diskProvider,
                                   ICacheManager cacheManager,
                                   Logger logger)
        {
            _sceneFolderLayout = sceneFolderLayout;
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _parsingService = parsingService;
            _importDecisionMaker = importDecisionMaker;
            _fileMatchEvaluator = fileMatchEvaluator;
            _fingerprintLookupClient = fingerprintLookupClient;
            _videoHashService = videoHashService;
            _diskProvider = diskProvider;
            _unidentifiedCache = cacheManager.GetCache<string>(GetType(), "unidentified");
            _logger = logger;
        }

        public List<ImportDecision> IdentifyOwned(Movie movie, List<string> unmappedVideoFiles)
        {
            var result = new List<ImportDecision>();

            // Files any item already tracks (movies included) are never candidates; unmapped rows don't count
            var candidates = _mediaFileService.FilterFilesTrackedByAnyItem(movie.Path, unmappedVideoFiles);
            var keys = candidates.ToDictionary(f => f, CacheKey, PathEqualityComparer.Instance);

            candidates = candidates.Where(f => _unidentifiedCache.Find(keys[f]) == null).ToList();

            if (candidates.Count == 0)
            {
                return result;
            }

            var scenes = GetScenesInFolder(movie);
            var owners = new Dictionary<string, Movie>(PathEqualityComparer.Instance);
            var needHash = new List<string>();

            foreach (var file in candidates)
            {
                var found = FindByName(file);

                if (found == null)
                {
                    needHash.Add(file);
                }
                else if (scenes.TryGetValue(found.Id, out var owner))
                {
                    owners[file] = owner;
                }

                // else: a scene outside this folder, which isn't this scan's to import and isn't "unidentified"
            }

            foreach (var file in IdentifyByHash(needHash, scenes, owners))
            {
                _unidentifiedCache.Set(keys[file], "1", UnidentifiedLifetime);
            }

            foreach (var group in owners.GroupBy(o => o.Value.Id))
            {
                var owner = group.First().Value;

                result.AddRange(_importDecisionMaker.GetImportDecisions(group.Select(g => g.Key).ToList(), owner, false));
            }

            return result;
        }

        private Dictionary<int, Movie> GetScenesInFolder(Movie movie)
        {
            var ids = _sceneFolderLayout.GetItemsInFolder(movie.Path)
                                        .Where(e => e.ItemType == ItemType.Scene && e.Id != movie.Id)
                                        .Select(e => e.Id)
                                        .ToList();

            var scenes = ids.Count == 0 ? new List<Movie>() : _movieService.GetMovies(ids);

            var map = scenes.Where(s => s.Id != movie.Id).ToDictionary(s => s.Id);
            map[movie.Id] = movie;

            return map;
        }

        // The scene the file name parses to, or null. Never searches online.
        private Movie FindByName(string file)
        {
            try
            {
                var info = _parsingService.ParseMinimalPathMovieInfo(file);

                return info == null ? null : _movieService.FindScene(info);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to identify {0} by name", file);

                return null;
            }
        }

        // Fills owners for the files whose hash maps to an in-folder scene; returns the files that matched no scene at all.
        private List<string> IdentifyByHash(List<string> files, Dictionary<int, Movie> scenes, Dictionary<string, Movie> owners)
        {
            var unidentified = new List<string>();

            if (files.Count == 0)
            {
                return unidentified;
            }

            var hashes = new Dictionary<string, string>(PathEqualityComparer.Instance);

            foreach (var file in files)
            {
                var (osHash, _) = _videoHashService.ComputeOsHashStamp(_diskProvider, file, _logger);

                if (osHash.IsNotNullOrWhiteSpace())
                {
                    hashes[file] = osHash;
                }
                else
                {
                    // No hash is no evidence either way: leave it uncached so the next scan retries
                    _logger.Debug("No oshash for {0}", file);
                }
            }

            // Local tier: the oshash of a file one of the folder's scenes already has
            var localByHash = scenes.Values
                .SelectMany(s => _mediaFileService.GetFilesByMovie(s.Id).Where(f => f.OsHash.IsNotNullOrWhiteSpace()).Select(f => (f.OsHash, Scene: s)))
                .GroupBy(x => x.OsHash)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Scene).DistinctBy(s => s.Id).ToList());

            var remote = new List<FileFingerprint>();

            foreach (var (file, osHash) in hashes)
            {
                if (localByHash.TryGetValue(osHash, out var local) && local.Count == 1)
                {
                    owners[file] = local[0];
                }
                else
                {
                    remote.Add(new FileFingerprint { Key = file, OsHash = osHash });
                }
            }

            var lookups = LookupLocal(remote);

            foreach (var print in remote)
            {
                var target = FindHashTarget(print.Key, lookups);

                if (target == null)
                {
                    unidentified.Add(print.Key);
                }
                else if (scenes.TryGetValue(target.Id, out var owner))
                {
                    owners[print.Key] = owner;
                }
            }

            return unidentified;
        }

        private Dictionary<string, FingerprintLookupResult> LookupLocal(List<FileFingerprint> files)
        {
            var empty = new Dictionary<string, FingerprintLookupResult>();

            if (files.Count == 0)
            {
                return empty;
            }

            try
            {
                // localOnly: never leaves the metadata service's own index
                return _fingerprintLookupClient.Lookup(files, true) ?? empty;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Fingerprint lookup failed for {0} files", files.Count);

                return empty;
            }
        }

        private Movie FindHashTarget(string file, Dictionary<string, FingerprintLookupResult> lookups)
        {
            if (!lookups.TryGetValue(file, out var lookup))
            {
                return null;
            }

            try
            {
                var outcome = _fileMatchEvaluator.Evaluate(null, lookup, null, false);

                return outcome is { Tier: MatchTier.Hash, Target.Id: > 0 } ? outcome.Target : null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to verify fingerprint match for {0}", file);

                return null;
            }
        }

        // Path + size + last-write: a changed file is identified afresh
        private string CacheKey(string file)
        {
            long size = 0;

            try
            {
                size = _diskProvider.GetFileSize(file);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read size of {0}", file);
            }

            var lastWrite = VideoHashExtensions.TryGetLastWrite(_diskProvider, file, _logger);

            return $"{file}|{size}|{lastWrite?.Ticks}";
        }
    }
}
