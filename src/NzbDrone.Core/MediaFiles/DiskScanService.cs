using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.LostFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDiskScanService
    {
        void Scan(Movie movie, bool manual = false);

        // Scans the items, each shared folder once for all of its requested scenes. Items are loaded a folder at a time.
        void Scan(IEnumerable<int> movieIds, bool manual = false);
        string[] GetVideoFiles(string path, bool allDirectories = true);
        string[] GetNonVideoFiles(string path, bool allDirectories = true);
        List<string> FilterPaths(string basePath, IEnumerable<string> paths, bool filterExtras = true);

        // Runs the action holding the movie's scan lock and then the folder's, the same locks Scan takes
        void RunLocked(Movie movie, string folder, Action action);
    }

    public class DiskScanService :
        IDiskScanService,
        IExecute<RescanMovieCommand>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedMovie _importApprovedMovies;
        private readonly IConfigService _configService;
        private readonly IMovieService _movieService;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMediaFileTableCleanupService _mediaFileTableCleanupService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IUpdateMediaInfo _updateMediaInfoService;
        private readonly IVideoHashService _videoHashService;
        private readonly ISceneFolderLayout _sceneFolderLayout;
        private readonly ISharedFolderScanner _sharedFolderScanner;
        private readonly ILostMovieFileService _lostMovieFileService;
        private readonly ISharedFolderSideFiles _sharedFolderSideFiles;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        // Per-movie in-process locks to ensure Scan runs only one concurrent scan per movie.Id
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> ScanLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

        // Per-folder locks so sibling scenes sharing a folder never import the same file twice. Always taken after the
        // movie lock. Entries are never removed: dropping one while a waiter holds it would let two scans in at once.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> FolderLocks = new ConcurrentDictionary<string, SemaphoreSlim>(PathEqualityComparer.Instance);

        // Every dependency is a distinct collaborator resolved by DI
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public DiskScanService(IDiskProvider diskProvider,
                               IMakeImportDecision importDecisionMaker,
                               IImportApprovedMovie importApprovedMovies,
                               IConfigService configService,
                               IMovieService movieService,
                               IMediaFileRepository mediaFileRepository,
                               IMediaFileService mediaFileService,
                               IMediaFileTableCleanupService mediaFileTableCleanupService,
                               IRootFolderService rootFolderService,
                               IUpdateMediaInfo updateMediaInfoService,
                               IVideoHashService videoHashService,
                               ISceneFolderLayout sceneFolderLayout,
                               ISharedFolderScanner sharedFolderScanner,
                               ILostMovieFileService lostMovieFileService,
                               ISharedFolderSideFiles sharedFolderSideFiles,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            _lostMovieFileService = lostMovieFileService;
            _sharedFolderSideFiles = sharedFolderSideFiles;
            _sceneFolderLayout = sceneFolderLayout;
            _sharedFolderScanner = sharedFolderScanner;
            _diskProvider = diskProvider;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedMovies = importApprovedMovies;
            _configService = configService;
            _movieService = movieService;
            _mediaFileRepository = mediaFileRepository;
            _mediaFileService = mediaFileService;
            _mediaFileTableCleanupService = mediaFileTableCleanupService;
            _rootFolderService = rootFolderService;
            _updateMediaInfoService = updateMediaInfoService;
            _videoHashService = videoHashService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private static readonly Regex ExcludedExtrasSubFolderRegex = new Regex(@"(?:\\|\/|^)(?:extras|extrafanart|behind the scenes|deleted scenes|featurettes|interviews|other|scenes|sample[s]?|shorts|trailers|theme[-_. ]music|backdrops)(?:\\|\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexDefaults.Timeout);
        private static readonly Regex ExcludedSubFoldersRegex = new Regex(@"(?:\\|\/|^)(?:@eadir|\.@__thumb|plex versions|\.[^\\/]+)(?:\\|\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexDefaults.Timeout);
        private static readonly Regex ExcludedExtraFilesRegex = new Regex(@"(-(trailer|other|behindthescenes|deleted|featurette|interview|scene|short)\.[^.]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexDefaults.Timeout);
        private static readonly Regex ExcludedFilesRegex = new Regex(@"^\.(_|unmanic|DS_Store$)|^Thumbs\.db$", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexDefaults.Timeout);

        public void Scan(Movie movie, bool manual = false)
        {
            if (movie == null)
            {
                return;
            }

            if (movie.Path.IsNotNullOrWhiteSpace() && _sceneFolderLayout.IsShared(movie))
            {
                // A single-scene rescan still identifies the whole folder, so a newly dropped file is found
                ScanSharedFolder(movie.Path, new List<Movie> { movie }, manual);
                return;
            }

            var sem = ScanLocks.GetOrAdd(movie.Id, _ => new SemaphoreSlim(1, 1));

            _logger.Trace("Acquiring scan lock for movie id {0} ({1})", movie.Id, movie.Title);
            sem.Wait();
            try
            {
                var rootFolder = _rootFolderService.GetBestRootFolderPath(movie.Path);

                var movieFolderExists = _diskProvider.FolderExists(movie.Path);

                if (!movieFolderExists)
                {
                    if (!_diskProvider.FolderExists(rootFolder))
                    {
                        _logger.Warn("Movie's root folder ({0}) doesn't exist.", rootFolder);
                        _eventAggregator.PublishEvent(new MovieScanSkippedEvent(movie, MovieScanSkippedReason.RootFolderDoesNotExist));
                        return;
                    }

                    if (_diskProvider.FolderEmpty(rootFolder))
                    {
                        _logger.Warn("Movie's root folder ({0}) is empty. Rescan will not update movies as a failsafe.", rootFolder);
                        _eventAggregator.PublishEvent(new MovieScanSkippedEvent(movie, MovieScanSkippedReason.RootFolderIsEmpty));
                        return;
                    }
                }

                _logger.Trace("Scanning disk for {0}", movie.Title);

                if (!movieFolderExists)
                {
                    ScanMissingFolder(movie);
                    return;
                }

                var videoFilesStopwatch = Stopwatch.StartNew();
                var mediaFileList = FilterPaths(movie.Path, GetVideoFiles(movie.Path)).ToList();
                videoFilesStopwatch.Stop();
                _logger.Trace("Finished getting movie files for: {0} [{1}]", movie, videoFilesStopwatch.Elapsed);

                var onDisk = new HashSet<string>(mediaFileList, PathEqualityComparer.Instance);

                RelinkRenamedFiles(movie, _mediaFileService.GetFilesByMovie(movie.Id), mediaFileList, onDisk, false, new Dictionary<string, (string OsHash, DateTime? HashedAt)>(PathEqualityComparer.Instance));

                RecordLostFiles(movie, null, onDisk);
                CleanMediaFiles(movie, mediaFileList);

                var movieFiles = _mediaFileService.GetFilesByMovie(movie.Id);
                var unmappedFiles = MediaFileService.FilterExistingFiles(mediaFileList, movieFiles, movie);

                var decisionsStopwatch = Stopwatch.StartNew();
                var decisions = _importDecisionMaker.GetImportDecisions(unmappedFiles, movie, false);
                decisionsStopwatch.Stop();
                _logger.Trace("Import decisions complete for: {0} [{1}]", movie, decisionsStopwatch.Elapsed);
                _importApprovedMovies.Import(decisions, false);

                if (IsScene(movie) && unmappedFiles.Count > 0 && _lostMovieFileService.Any())
                {
                    // Only what no item tracks is unowned, whatever the layout
                    RelinkLostFiles(_mediaFileService.FilterFilesTrackedByAnyItem(movie.Path, unmappedFiles), decisions);
                }

                UpdateExistingFiles(movie, movieFiles, manual);

                var filesOnDisk = GetNonVideoFiles(movie.Path);
                var possibleExtraFiles = FilterPaths(movie.Path, filesOnDisk);

                RemoveEmptyMovieFolder(movie);

                // Check if movie is in the scene layout, then cleanup the unmapped file
                if (movie.MovieMetadata.Value.ItemType.UsesSceneLayout())
                {
                    var existingUnmappedFiles = _mediaFileRepository.GetUnmappedFiles();
                    if (existingUnmappedFiles != null)
                    {
                        foreach (var unmappedFile in existingUnmappedFiles)
                        {
                            var owned = unmappedFile.OriginalFilePath.IsNotNullOrWhiteSpace() && movie.Path.IsParentPath(unmappedFile.OriginalFilePath);

                            if (owned)
                            {
                                var reason = DeleteMediaFileReason.Manual;
                                _mediaFileRepository.Delete(unmappedFile);
                                _eventAggregator.PublishEvent(new MovieFileDeletedEvent(unmappedFile, reason));
                                break;
                            }
                        }
                    }
                }

                CompletedScanning(movie, possibleExtraFiles);
            }
            finally
            {
                ReleaseScanLock(movie, sem);
            }
        }

        public void Scan(IEnumerable<int> movieIds, bool manual = false)
        {
            var ids = movieIds.Distinct().ToList();
            var entries = _sceneFolderLayout.GetEntries(ids).Where(e => e.Path.IsNotNullOrWhiteSpace()).ToList();
            var indexed = entries.Select(e => e.Id).ToHashSet();
            var single = new List<int>();

            foreach (var folder in entries.GroupBy(e => e.Path, PathEqualityComparer.Instance))
            {
                var folderIds = folder.Select(e => e.Id).ToList();

                if (folderIds.Count == 1)
                {
                    single.Add(folderIds[0]);
                    continue;
                }

                TryScan(folder.Key, () => ScanFolder(folderIds, manual));
            }

            // Alone in their folder, not in the path index yet, or without a path: each is scanned on its own
            // (Scan(movie) still takes the shared-folder pass when it is shared), loaded in chunks
            single.AddRange(ids.Where(id => !indexed.Contains(id)));

            foreach (var chunk in single.Chunk(1000))
            {
                TryScan($"{chunk.Length} item(s)", () =>
                {
                    foreach (var movie in LoadMovies(chunk.ToList()))
                    {
                        TryScan(movie.ToString(), () => Scan(movie, manual));
                    }
                });
            }
        }

        private void ScanFolder(List<int> ids, bool manual)
        {
            var movies = LoadMovies(ids);
            var shared = movies.Where(m => m.Path.IsNotNullOrWhiteSpace() && _sceneFolderLayout.IsShared(m)).ToList();

            // Grouped by the loaded path, which the cached index can lag behind
            foreach (var folder in shared.GroupBy(m => m.Path, PathEqualityComparer.Instance))
            {
                TryScan(folder.Key, () => ScanSharedFolder(folder.Key, folder.ToList(), manual));
            }

            foreach (var movie in movies.Except(shared))
            {
                TryScan(movie.ToString(), () => Scan(movie, manual));
            }
        }

        private List<Movie> LoadMovies(List<int> ids)
        {
            var movies = _movieService.GetMovies(ids);
            var loaded = movies.Select(m => m.Id).ToHashSet();

            foreach (var id in ids.Where(id => !loaded.Contains(id)))
            {
                _logger.Debug("Item {0} no longer exists, not scanning it", id);
            }

            return movies;
        }

        private void TryScan(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to scan {0}", what);
            }
        }

        // Each scene under its own movie lock then the folder lock (R2), so one failing doesn't stop its siblings
        private void ForEachScene(string folder, List<Movie> scenes, Action<Movie> action)
        {
            foreach (var scene in scenes)
            {
                TryScan(scene.ToString(), () => RunLocked(scene, folder, () => action(scene)));
            }
        }

        /// <summary>
        /// Scans a shared folder once for the requested scenes: one listing, one identification of the unowned videos,
        /// then each requested scene is finished. Folder-level steps hold only the folder lock and per-scene steps run
        /// through RunLocked one after another, so no movie lock is ever taken while the folder lock is held (R2).
        /// </summary>
        private void ScanSharedFolder(string folder, List<Movie> requested, bool manual)
        {
            var rootFolder = _rootFolderService.GetBestRootFolderPath(folder);

            if (!_diskProvider.FolderExists(folder))
            {
                if (!_diskProvider.FolderExists(rootFolder))
                {
                    _logger.Warn("Movie's root folder ({0}) doesn't exist.", rootFolder);
                    requested.ForEach(m => _eventAggregator.PublishEvent(new MovieScanSkippedEvent(m, MovieScanSkippedReason.RootFolderDoesNotExist)));
                    return;
                }

                if (_diskProvider.FolderEmpty(rootFolder))
                {
                    _logger.Warn("Movie's root folder ({0}) is empty. Rescan will not update movies as a failsafe.", rootFolder);
                    requested.ForEach(m => _eventAggregator.PublishEvent(new MovieScanSkippedEvent(m, MovieScanSkippedReason.RootFolderIsEmpty)));
                    return;
                }

                ForEachScene(folder, requested, ScanMissingFolder);
                return;
            }

            _logger.Trace("Scanning shared folder {0} for {1} item(s)", folder, requested.Count);

            List<string> videoFiles = null;
            List<string> possibleExtraFiles = null;
            ILookup<int, MovieFile> rows = null;

            RunInFolderLock(folder, () =>
            {
                var filesOnDisk = _diskProvider.GetFiles(folder, true).ToList();

                videoFiles = FilterPaths(folder, filesOnDisk.Where(f => MediaFileExtensions.Extensions.Contains(Path.GetExtension(f))));
                possibleExtraFiles = FilterPaths(folder, filesOnDisk.Where(f => !MediaFileExtensions.Extensions.Contains(Path.GetExtension(f))));

                // Every requested scene's rows in one query; later steps reuse these objects, so a re-link is seen
                rows = _mediaFileService.GetFilesByMovies(requested.Select(m => m.Id)).ToLookup(f => f.MovieId);
            });

            var hashes = new Dictionary<string, (string OsHash, DateTime? HashedAt)>(PathEqualityComparer.Instance);
            var movieFiles = new Dictionary<int, List<MovieFile>>();

            // Built once per pass: a flat root lists every scene's files, so a per-scene set is quadratic
            var onDisk = new HashSet<string>(videoFiles, PathEqualityComparer.Instance);

            ForEachScene(folder, requested, scene =>
            {
                var sceneFiles = rows[scene.Id].ToList();

                RelinkRenamedFiles(scene, sceneFiles, videoFiles, onDisk, true, hashes);
                RecordLostFiles(scene, sceneFiles, onDisk);

                _logger.Debug("{0} Cleaning up media files in DB", scene);
                var removed = _mediaFileTableCleanupService.Clean(scene, onDisk, sceneFiles) ?? new List<MovieFile>();

                movieFiles[scene.Id] = sceneFiles.Except(removed).ToList();
            });

            RunInFolderLock(folder, () =>
            {
                // Files any item tracks are never unowned: siblings, nested items and an item above the folder.
                // IdentifyOwned(folder, ...) trusts this, so the filter runs once per pass.
                var unowned = _mediaFileService.FilterFilesTrackedByAnyItem(folder, videoFiles);

                var decisions = _sharedFolderScanner.IdentifyOwned(folder, unowned);
                _importApprovedMovies.Import(decisions, false);

                if (unowned.Count > 0 && requested.Any(IsScene) && _lostMovieFileService.Any())
                {
                    RelinkLostFiles(unowned, decisions);
                }

                RemoveImportedUnmappedFiles(decisions);
            });

            SideFileAssignment sideFiles = null;

            RunInFolderLock(folder, () =>
            {
                // Every item in the folder, so a sibling's tracked side file is never parsed; the requested scenes too,
                // in case the path index doesn't list them yet. Video rows are read after this pass's imports.
                var itemIds = _sceneFolderLayout.GetItemsInFolder(folder).Select(e => e.Id).Union(requested.Select(m => m.Id)).ToList();

                sideFiles = _sharedFolderSideFiles.Assign(folder, itemIds, possibleExtraFiles, _mediaFileService.GetFilesByMovies(itemIds));
            });

            // A scene whose earlier steps failed isn't finished
            ForEachScene(folder, requested.Where(s => movieFiles.ContainsKey(s.Id)).ToList(), scene =>
            {
                UpdateExistingFiles(scene, movieFiles[scene.Id], manual);
                CompletedScanning(scene, sideFiles.FilesByMovie[scene.Id], sideFiles.OwnerFileIds);
            });

            RunInFolderLock(folder, () => RemoveEmptyMovieFolder(requested[0]));
        }

        private void ScanMissingFolder(Movie movie)
        {
            if (_configService.CreateEmptyMovieFolders)
            {
                if (_configService.DeleteEmptyFolders)
                {
                    _logger.Debug("Not creating missing movie folder: {0} because delete empty movie folders is enabled", movie.Path);
                }
                else
                {
                    _logger.Debug("Creating missing movie folder: {0}", movie.Path);

                    _diskProvider.CreateFolder(movie.Path);
                    SetPermissions(movie.Path);
                }
            }
            else
            {
                _logger.Debug("Movie's folder doesn't exist: {0}", movie.Path);
            }

            RecordLostFiles(movie, null, new HashSet<string>());
            CleanMediaFiles(movie, new List<string>());
            CompletedScanning(movie, new List<string>());
        }

        // Every unmapped row for a file this pass imported, whichever scene took it; a row for any other file stays
        private void RemoveImportedUnmappedFiles(List<ImportDecision> decisions)
        {
            var importedPaths = decisions.Where(d => d.Approved && d.LocalMovie.Path.IsNotNullOrWhiteSpace())
                                         .Select(d => d.LocalMovie.Path)
                                         .ToHashSet(PathEqualityComparer.Instance);

            if (importedPaths.Count == 0)
            {
                return;
            }

            var unmappedFiles = _mediaFileRepository.GetUnmappedFiles() ?? new List<MovieFile>();

            foreach (var unmappedFile in unmappedFiles.Where(f => f.OriginalFilePath.IsNotNullOrWhiteSpace() && importedPaths.Contains(f.OriginalFilePath)))
            {
                _mediaFileRepository.Delete(unmappedFile);
                _eventAggregator.PublishEvent(new MovieFileDeletedEvent(unmappedFile, DeleteMediaFileReason.Manual));
            }
        }

        // Update existing files that have a different file size
        private void UpdateExistingFiles(Movie movie, List<MovieFile> movieFiles, bool manual)
        {
            var fileInfoStopwatch = Stopwatch.StartNew();
            var filesToUpdate = new List<MovieFile>();

            foreach (var file in movieFiles)
            {
                var path = Path.Combine(movie.Path, file.RelativePath);
                var fileSize = _diskProvider.GetFileSize(path);

                var hashesChanged = UpdateHashes(file, path, fileSize);

                if (!manual && file.Size == fileSize)
                {
                    if (hashesChanged)
                    {
                        filesToUpdate.Add(file);
                    }

                    continue;
                }

                file.Size = fileSize;

                if (!_updateMediaInfoService.Update(file, movie))
                {
                    filesToUpdate.Add(file);
                }
            }

            // Update any files that had a file size change, but didn't get media info updated.
            if (filesToUpdate.Any())
            {
                _mediaFileService.Update(filesToUpdate);
            }

            fileInfoStopwatch.Stop();
            _logger.Trace("Reprocessing existing files complete for: {0} [{1}]", movie, fileInfoStopwatch.Elapsed);
        }

        public void RunLocked(Movie movie, string folder, Action action)
        {
            var sem = ScanLocks.GetOrAdd(movie.Id, _ => new SemaphoreSlim(1, 1));

            _logger.Trace("Acquiring scan lock for movie id {0} ({1})", movie.Id, movie.Title);
            sem.Wait();
            try
            {
                // R2: movie lock (held), then the folder lock
                RunInFolderLock(folder, action);
            }
            finally
            {
                ReleaseScanLock(movie, sem);
            }
        }

        private void ReleaseScanLock(Movie movie, SemaphoreSlim sem)
        {
            try
            {
                sem.Release();
            }
            catch (SemaphoreFullException)
            {
                // Shouldn't happen, but guard against releasing too many times.
            }

            // Attempt to remove the semaphore from the dictionary when it looks unused.
            // This is best-effort and racing callers may re-create the entry; that's acceptable.
            if (ScanLocks.TryGetValue(movie.Id, out var existing) && ReferenceEquals(existing, sem) && sem.CurrentCount == 1)
            {
                ScanLocks.TryRemove(movie.Id, out _);
            }

            _logger.Trace("Released scan lock for movie id {0} ({1})", movie.Id, movie.Title);
        }

        /// <summary>
        /// Clears stale hashes when the file changed on disk (size or last-write), then computes the oshash if missing.
        /// Must run before file.Size is updated. Returns true when the file's hash fields changed and need saving.
        /// </summary>
        private bool UpdateHashes(MovieFile file, string path, long fileSize)
        {
            var lastWrite = VideoHashExtensions.TryGetLastWrite(_diskProvider, path, _logger);
            var changed = false;

            if (file.Size != fileSize)
            {
                ClearHashes(file);
                changed = true;
            }
            else if (file.HashedFileModifiedAt.HasValue && lastWrite.HasValue && !VideoHashExtensions.SameLastWrite(file.HashedFileModifiedAt.Value, lastWrite.Value))
            {
                // Only the mtime moved (copy, touch, FileDate rewrite): the content is unchanged if the oshash still matches
                var (newHash, newHashedAt) = _videoHashService.ComputeOsHashStamp(_diskProvider, path, _logger);

                if (newHash == null)
                {
                    // A transient read error says nothing about the content: keep every hash, the next scan retries
                    return false;
                }

                if (newHash == file.OsHash)
                {
                    file.HashedFileModifiedAt = newHashedAt ?? lastWrite;
                }
                else
                {
                    ClearHashes(file);
                    file.OsHash = newHash;
                    file.HashedFileModifiedAt = newHashedAt;
                }

                return true;
            }

            if (file.OsHash == null)
            {
                var (osHash, hashedAt) = _videoHashService.ComputeOsHashStamp(_diskProvider, path, _logger);

                if (osHash != null)
                {
                    file.OsHash = osHash;
                    file.HashedFileModifiedAt = hashedAt;
                    changed = true;
                }
            }
            else if (file.HashedFileModifiedAt == null && lastWrite.HasValue)
            {
                file.HashedFileModifiedAt = lastWrite;
                changed = true;
            }

            return changed;
        }

        // Always taken after the movie lock (R2)
        private void RunInFolderLock(string folder, Action action)
        {
            var folderLock = FolderLocks.GetOrAdd(folder, _ => new SemaphoreSlim(1, 1));

            _logger.Trace("Acquiring folder scan lock for {0}", folder);
            folderLock.Wait();
            try
            {
                action();
            }
            finally
            {
                try
                {
                    folderLock.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Shouldn't happen, but guard against releasing too many times.
                }

                _logger.Trace("Released folder scan lock for {0}", folder);
            }
        }

        /// <summary>
        /// Re-points a MovieFile whose path is missing at the unknown file in the movie folder that has the same oshash
        /// (a rename outside Whisparr). Only an unambiguous 1:1 match is re-linked; nothing on disk is touched.
        /// </summary>
        private void RelinkRenamedFiles(Movie movie, List<MovieFile> movieFiles, List<string> mediaFileList, HashSet<string> onDisk, bool shared, Dictionary<string, (string OsHash, DateTime? HashedAt)> hashCache)
        {
            // The listing can miss a file that is still there (filtered path, unreadable folder), so confirm on disk
            var missing = movieFiles
                .Where(f => f.OsHash != null &&
                            !onDisk.Contains(Path.Combine(movie.Path, f.RelativePath)) &&
                            !_diskProvider.FileExists(Path.Combine(movie.Path, f.RelativePath)))
                .ToList();

            if (missing.Count == 0)
            {
                return;
            }

            var newFiles = MediaFileService.FilterExistingFiles(mediaFileList, movieFiles, movie);

            if (shared)
            {
                // No item's tracked file is up for re-linking (siblings, nested items, an item above), even when its hash matches
                newFiles = _mediaFileService.FilterFilesTrackedByAnyItem(movie.Path, newFiles);
            }

            if (newFiles.Count == 0)
            {
                return;
            }

            var newFileHashes = new Dictionary<string, (string OsHash, DateTime? HashedAt)>();

            foreach (var path in newFiles)
            {
                // Shared across a folder pass's scenes, so each candidate is hashed at most once
                if (!hashCache.TryGetValue(path, out var hash))
                {
                    hash = _videoHashService.ComputeOsHashStamp(_diskProvider, path, _logger);
                    hashCache[path] = hash;
                }

                if (hash.OsHash != null)
                {
                    newFileHashes[path] = hash;
                }
            }

            foreach (var row in missing)
            {
                if (missing.Count(m => m.OsHash == row.OsHash) != 1)
                {
                    continue;
                }

                var matches = newFileHashes.Where(h => h.Value.OsHash == row.OsHash).ToList();

                if (matches.Count != 1)
                {
                    continue;
                }

                var newPath = matches[0].Key;
                var oldRelativePath = row.RelativePath;

                row.RelativePath = movie.Path.GetRelativePath(newPath);
                row.HashedFileModifiedAt = matches[0].Value.HashedAt;
                _mediaFileService.Update(row);

                _logger.Info("Re-linked {0} to {1} by oshash", oldRelativePath, row.RelativePath);
            }
        }

        private static bool IsScene(Movie movie)
        {
            return movie.MovieMetadata?.Value?.ItemType == ItemType.Scene;
        }

        /// <summary>
        /// Remembers the oshash of each scene file that is gone from disk (and wasn't re-linked in its folder), so a later
        /// scan can follow it if another tool moved it to a different library folder. Runs before the rows are deleted.
        /// </summary>
        // movieFiles null: the scene's rows are loaded here, only when it is a scene
        private void RecordLostFiles(Movie movie, List<MovieFile> movieFiles, HashSet<string> onDisk)
        {
            if (!IsScene(movie))
            {
                return;
            }

            try
            {
                foreach (var file in movieFiles ?? _mediaFileService.GetFilesByMovie(movie.Id))
                {
                    var path = Path.Combine(movie.Path, file.RelativePath);

                    if (file.OsHash != null && !onDisk.Contains(path) && !_diskProvider.FileExists(path))
                    {
                        _lostMovieFileService.Record(movie, file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to remember the missing files of {0}", movie);
            }
        }

        /// <summary>
        /// Files in a scene folder that no item tracks may be a file StashApp moved here from another scene's folder:
        /// re-link them, in place, to the scene that lost a file with the same oshash and size. In a shared folder this
        /// runs under the folder lock, so a sibling's scan can't claim the same file meanwhile.
        /// </summary>
        private void RelinkLostFiles(List<string> candidates, List<ImportDecision> decisions)
        {
            // Only approved decisions are claimed (imported, so tracked); a rejected file is still unowned
            var decided = new HashSet<string>(decisions.Where(d => d.Approved).Select(d => d.LocalMovie.Path), PathEqualityComparer.Instance);

            foreach (var path in candidates.Where(c => !decided.Contains(c)))
            {
                try
                {
                    // Each unowned file is hashed at most once per cache window
                    if (!_sharedFolderScanner.TryBeginLostFileCheck(path))
                    {
                        continue;
                    }

                    var (osHash, _) = _videoHashService.ComputeOsHashStamp(_diskProvider, path, _logger);

                    if (osHash.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    _lostMovieFileService.TryRelink(path, osHash, _diskProvider.GetFileSize(path));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Unable to check {0} against lost files", path);
                }
            }
        }

        private static void ClearHashes(MovieFile file)
        {
            file.OsHash = null;
            file.PHash = null;
            file.PHashAttemptedAt = null;
            file.HashedFileModifiedAt = null;
        }

        private void CleanMediaFiles(Movie movie, List<string> mediaFileList)
        {
            _logger.Debug("{0} Cleaning up media files in DB", movie);
            _mediaFileTableCleanupService.Clean(movie, mediaFileList);
        }

        private void CompletedScanning(Movie movie, List<string> possibleExtraFiles, Dictionary<string, int> sideFileOwners = null)
        {
            _logger.Trace("Completed scanning disk for {0}", movie.Title);
            _eventAggregator.PublishEvent(new MovieScannedEvent(movie, possibleExtraFiles) { SideFileOwners = sideFileOwners });
        }

        public string[] GetVideoFiles(string path, bool allDirectories = true)
        {
            _logger.Debug("Scanning '{0}' for video files", path);

            var filesOnDisk = _diskProvider.GetFiles(path, allDirectories).ToList();

            var mediaFileList = filesOnDisk.Where(file => MediaFileExtensions.Extensions.Contains(Path.GetExtension(file)))
                                           .ToList();

            _logger.Trace("{0} files were found in {1}", filesOnDisk.Count, path);
            _logger.Debug("{0} video files were found in {1}", mediaFileList.Count, path);

            return mediaFileList.ToArray();
        }

        public string[] GetNonVideoFiles(string path, bool allDirectories = true)
        {
            _logger.Debug("Scanning '{0}' for non-video files", path);

            var filesOnDisk = _diskProvider.GetFiles(path, allDirectories).ToList();

            var mediaFileList = filesOnDisk.Where(file => !MediaFileExtensions.Extensions.Contains(Path.GetExtension(file)))
                                           .ToList();

            _logger.Trace("{0} files were found in {1}", filesOnDisk.Count, path);
            _logger.Debug("{0} non-video files were found in {1}", mediaFileList.Count, path);

            return mediaFileList.ToArray();
        }

        public List<string> FilterPaths(string basePath, IEnumerable<string> paths, bool filterExtras = true)
        {
            var filteredPaths =  paths.Where(path => !ExcludedSubFoldersRegex.IsMatch(basePath.GetRelativePath(path)))
                                      .Where(path => !ExcludedFilesRegex.IsMatch(Path.GetFileName(path)))
                                      .ToList();

            if (filterExtras)
            {
                filteredPaths = filteredPaths.Where(path => !ExcludedExtrasSubFolderRegex.IsMatch(basePath.GetRelativePath(path)))
                                             .Where(path => !ExcludedExtraFilesRegex.IsMatch(Path.GetFileName(path)))
                                             .ToList();
            }

            return filteredPaths;
        }

        private void SetPermissions(string path)
        {
            if (!_configService.SetPermissionsLinux)
            {
                return;
            }

            try
            {
                _diskProvider.SetPermissions(path, _configService.ChmodFolder, _configService.ChownGroup);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to apply permissions to: " + path);
                _logger.Debug(ex, ex.Message);
            }
        }

        private void RemoveEmptyMovieFolder(Movie movie)
        {
            var path = movie.Path;

            if (_configService.DeleteEmptyFolders)
            {
                // An empty subfolder may be another item's own folder, so it is not swept
                if (!_sceneFolderLayout.HoldsOtherItems(movie.Id, path))
                {
                    _diskProvider.RemoveEmptySubfolders(path);
                }

                if (_diskProvider.FolderEmpty(path))
                {
                    _diskProvider.DeleteFolder(path, true);
                }
            }
        }

        public void Execute(RescanMovieCommand message)
        {
            if (message.MovieId.HasValue)
            {
                var movie = _movieService.GetMovie(message.MovieId.Value);
                Scan(movie);
            }
            else
            {
                Scan(_movieService.AllMovieIds());
            }
        }
    }
}
