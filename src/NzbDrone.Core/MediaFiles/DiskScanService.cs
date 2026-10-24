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
        string[] GetVideoFiles(string path, bool allDirectories = true);
        string[] GetNonVideoFiles(string path, bool allDirectories = true);
        List<string> FilterPaths(string basePath, IEnumerable<string> paths, bool filterExtras = true);
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
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            _lostMovieFileService = lostMovieFileService;
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

                    RecordLostFiles(movie, new List<string>());
                    CleanMediaFiles(movie, new List<string>());
                    CompletedScanning(movie, new List<string>());

                    return;
                }

                var videoFilesStopwatch = Stopwatch.StartNew();
                var mediaFileList = FilterPaths(movie.Path, GetVideoFiles(movie.Path)).ToList();
                videoFilesStopwatch.Stop();
                _logger.Trace("Finished getting movie files for: {0} [{1}]", movie, videoFilesStopwatch.Elapsed);

                var shared = movie.Path.IsNotNullOrWhiteSpace() && _sceneFolderLayout.IsShared(movie);

                RelinkRenamedFiles(movie, mediaFileList, shared);

                RecordLostFiles(movie, mediaFileList);
                CleanMediaFiles(movie, mediaFileList);

                var movieFiles = _mediaFileService.GetFilesByMovie(movie.Id);
                var unmappedFiles = MediaFileService.FilterExistingFiles(mediaFileList, movieFiles, movie);

                var decisionsStopwatch = Stopwatch.StartNew();
                List<ImportDecision> decisions;

                if (shared)
                {
                    decisions = IdentifyAndImportOwned(movie, unmappedFiles);
                    decisionsStopwatch.Stop();
                    _logger.Trace("Shared folder import complete for: {0} [{1}]", movie, decisionsStopwatch.Elapsed);
                }
                else
                {
                    decisions = _importDecisionMaker.GetImportDecisions(unmappedFiles, movie, false);
                    decisionsStopwatch.Stop();
                    _logger.Trace("Import decisions complete for: {0} [{1}]", movie, decisionsStopwatch.Elapsed);
                    _importApprovedMovies.Import(decisions, false);
                }

                RelinkLostFiles(movie, unmappedFiles, decisions, shared);

                // Update existing files that have a different file size
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
                _logger.Trace("Reprocessing existing files complete for: {0} [{1}]", movie, decisionsStopwatch.Elapsed);

                var filesOnDisk = GetNonVideoFiles(movie.Path);
                var possibleExtraFiles = FilterPaths(movie.Path, filesOnDisk);

                RemoveEmptyMovieFolder(movie);

                // Check if movie is a scene, then cleanup the unmapped scene
                if (movie.MovieMetadata.Value.ItemType == ItemType.Scene)
                {
                    var existingUnmappedFiles = _mediaFileRepository.GetUnmappedFiles();
                    if (existingUnmappedFiles != null)
                    {
                        // In a shared folder only the files this scan imported count; a row for a sibling's file stays
                        var importedPaths = shared
                            ? decisions.Where(d => d.Approved).Select(d => d.LocalMovie.Path).ToList()
                            : null;

                        foreach (var unmappedFile in existingUnmappedFiles)
                        {
                            var owned = shared
                                ? importedPaths.Contains(unmappedFile.OriginalFilePath, PathEqualityComparer.Instance)
                                : unmappedFile.OriginalFilePath.Contains(movie.Path);

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

        // Shared folder: identify what this scene (or a sibling) owns and import it, one scan per folder at a time.
        private List<ImportDecision> IdentifyAndImportOwned(Movie movie, List<string> unmappedFiles)
        {
            List<ImportDecision> decisions = null;

            RunInFolderLock(movie.Path, () =>
            {
                decisions = _sharedFolderScanner.IdentifyOwned(movie, unmappedFiles);
                _importApprovedMovies.Import(decisions, false);
            });

            return decisions;
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
        private void RelinkRenamedFiles(Movie movie, List<string> mediaFileList, bool shared)
        {
            var movieFiles = _mediaFileService.GetFilesByMovie(movie.Id);
            var onDisk = new HashSet<string>(mediaFileList, PathEqualityComparer.Instance);

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
                // A sibling's tracked file is not up for re-linking, even when its hash matches
                var siblingFiles = _sceneFolderLayout.GetItemsInFolder(movie.Path)
                    .Where(i => i.Id != movie.Id)
                    .SelectMany(i => _mediaFileService.GetFilesByMovie(i.Id).Select(f => Path.Combine(i.Path, f.RelativePath)))
                    .ToList();

                newFiles = newFiles.Except(siblingFiles, PathEqualityComparer.Instance).ToList();
            }

            if (newFiles.Count == 0)
            {
                return;
            }

            var newFileHashes = new Dictionary<string, (string OsHash, DateTime? HashedAt)>();

            foreach (var path in newFiles)
            {
                var (osHash, hashedAt) = _videoHashService.ComputeOsHashStamp(_diskProvider, path, _logger);

                if (osHash != null)
                {
                    newFileHashes[path] = (osHash, hashedAt);
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
        private void RecordLostFiles(Movie movie, List<string> mediaFileList)
        {
            if (!IsScene(movie))
            {
                return;
            }

            try
            {
                var onDisk = new HashSet<string>(mediaFileList, PathEqualityComparer.Instance);

                foreach (var file in _mediaFileService.GetFilesByMovie(movie.Id))
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
        /// Files in this scene's folder that nothing claimed may be a file StashApp moved here from another scene's
        /// folder: re-link them, in place, to the scene that lost a file with the same oshash and size.
        /// </summary>
        private void RelinkLostFiles(Movie movie, List<string> unmappedFiles, List<ImportDecision> decisions, bool shared)
        {
            if (!IsScene(movie) || unmappedFiles.Count == 0 || !_lostMovieFileService.Any())
            {
                return;
            }

            if (shared)
            {
                // Under the folder lock a sibling's scan can't claim the same file meanwhile
                RunInFolderLock(movie.Path, () => RelinkLostFilesLocked(movie, unmappedFiles, decisions));
            }
            else
            {
                RelinkLostFilesLocked(movie, unmappedFiles, decisions);
            }
        }

        private void RelinkLostFilesLocked(Movie movie, List<string> unmappedFiles, List<ImportDecision> decisions)
        {
            // Only what no item tracks is unowned, whatever the layout
            var candidates = _mediaFileService.FilterFilesTrackedByAnyItem(movie.Path, unmappedFiles);
            var decided = new HashSet<string>(decisions.Select(d => d.LocalMovie.Path), PathEqualityComparer.Instance);

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

        private void CompletedScanning(Movie movie, List<string> possibleExtraFiles)
        {
            _logger.Trace("Completed scanning disk for {0}", movie.Title);
            _eventAggregator.PublishEvent(new MovieScannedEvent(movie, possibleExtraFiles));
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
                var allMovies = _movieService.GetAllMovies();

                foreach (var movie in allMovies)
                {
                    Scan(movie);
                }
            }
        }
    }
}
