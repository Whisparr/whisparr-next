using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileTableCleanupService
    {
        void Clean(Movie movie, List<string> filesOnDisk);

        // The same over rows the caller already loaded and a path-comparer set of the files on disk; returns the rows it removed
        List<MovieFile> Clean(Movie movie, HashSet<string> filesOnDisk, List<MovieFile> movieFiles);
        void Clean(string folder, List<string> filesOnDisk);
    }

    public class MediaFileTableCleanupService :
        IMediaFileTableCleanupService,
        IExecute<CleanUnmappedFilesCommand>
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public MediaFileTableCleanupService(IMediaFileService mediaFileService,
                                            IMovieService movieService,
                                            IEventAggregator eventAggregator,
                                            IDiskProvider diskProvider,
                                            Logger logger)
        {
            _mediaFileService = mediaFileService;
            _movieService = movieService;
            _eventAggregator = eventAggregator;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Clean(Movie movie, List<string> filesOnDisk)
        {
            Clean(movie, new HashSet<string>(filesOnDisk, PathEqualityComparer.Instance), _mediaFileService.GetFilesByMovie(movie.Id));
        }

        public List<MovieFile> Clean(Movie movie, HashSet<string> filesOnDisk, List<MovieFile> movieFiles)
        {
            var removed = new List<MovieFile>();

            foreach (var movieFile in movieFiles)
            {
                var movieFilePath = Path.Combine(movie.Path, movieFile.RelativePath);

                try
                {
                    if (filesOnDisk.Contains(movieFilePath))
                    {
                        continue;
                    }

                    // The rows can be a snapshot another scan has since re-linked or removed, so re-read the row
                    var current = FindCurrent(movieFile.Id);

                    if (current == null ||
                        current.MovieId != movieFile.MovieId ||
                        !Path.Combine(movie.Path, current.RelativePath).PathEquals(movieFilePath))
                    {
                        _logger.Debug("File [{0}] changed in db since it was read, not removing it", movieFilePath);
                        continue;
                    }

                    // A listing can be stale or partial (shared folders), so confirm on disk right before deleting.
                    if (!_diskProvider.FileExists(movieFilePath))
                    {
                        _logger.Debug("File [{0}] no longer exists on disk, removing from db", movieFilePath);
                        _mediaFileService.Delete(current, DeleteMediaFileReason.MissingFromDisk);
                        removed.Add(movieFile);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    var errorMessage = string.Format("Unable to cleanup MovieFile in DB: {0}", movieFile.Id);
                    _logger.Error(ex, errorMessage);
                }
            }

            return removed;
        }

        private MovieFile FindCurrent(int id)
        {
            try
            {
                return _mediaFileService.GetMovie(id);
            }
            catch (ModelNotFoundException)
            {
                return null;
            }
        }

        public void Clean(string folder, List<string> filesOnDisk)
        {
            var dbFiles = _mediaFileService.GetFilesWithBasePath(folder);

            // get files in database that are missing on disk and remove from database
            var missingFiles = dbFiles.ExceptBy(x => x.OriginalFilePath, filesOnDisk, x => x, PathEqualityComparer.Instance).ToList();

            _logger.Debug("The following files no longer exist on disk, removing from db:\n{0}",
                          string.Join("\n", missingFiles.Select(x => x.OriginalFilePath)));

            _mediaFileService.DeleteMany(missingFiles, DeleteMediaFileReason.MissingFromDisk);

            // get any movies matched to these moviefiles and unlink them
            var orphanedMovies = _movieService.GetMoviesByFileId(missingFiles.Select(x => x.Id));
            orphanedMovies.ForEach(x => x.MovieFileId = 0);
            _movieService.SetFileIds(orphanedMovies);
        }

        public void Execute(CleanUnmappedFilesCommand message)
        {
            var unmappedFiles = _mediaFileService.GetUnmappedFiles();
            _mediaFileService.DeleteMany(unmappedFiles, DeleteMediaFileReason.Manual);
            _eventAggregator.PublishEvent(new CleanCompletedEvent());
        }
    }
}
