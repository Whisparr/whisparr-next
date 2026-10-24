using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Movies
{
    public class MoveMovieService : IExecute<MoveMovieCommand>, IExecute<BulkMoveMovieCommand>
    {
        private readonly IMovieService _movieService;
        private readonly IBuildFileNames _filenameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ISceneFolderLayout _sceneFolderLayout;
        private readonly IMediaFileService _mediaFileService;
        private readonly ISubtitleFileService _subtitleFileService;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly IMetadataFileService _metadataFileService;
        private readonly Logger _logger;

        public MoveMovieService(IMovieService movieService,
                                 IBuildFileNames filenameBuilder,
                                 IDiskProvider diskProvider,
                                 IDiskTransferService diskTransferService,
                                 IEventAggregator eventAggregator,
                                 ISceneFolderLayout sceneFolderLayout,
                                 IMediaFileService mediaFileService,
                                 ISubtitleFileService subtitleFileService,
                                 IOtherExtraFileService otherExtraFileService,
                                 IMetadataFileService metadataFileService,
                                 Logger logger)
        {
            _sceneFolderLayout = sceneFolderLayout;
            _mediaFileService = mediaFileService;
            _subtitleFileService = subtitleFileService;
            _otherExtraFileService = otherExtraFileService;
            _metadataFileService = metadataFileService;
            _movieService = movieService;
            _filenameBuilder = filenameBuilder;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private void MoveSingleMovie(Movie movie, string sourcePath, string destinationPath, int? index = null, int? total = null)
        {
            if (!sourcePath.IsPathValid(PathValidationType.CurrentOs))
            {
                _logger.Warn("Folder '{0}' for '{1}' is invalid, unable to move movie. Try moving files manually", sourcePath, movie.Title);
                return;
            }

            if (!_diskProvider.FolderExists(sourcePath))
            {
                _logger.Debug("Folder '{0}' for '{1}' does not exist, not moving.", sourcePath, movie.Title);
                return;
            }

            if (index != null && total != null)
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}' ({3}/{4})", movie.Title, sourcePath, destinationPath, index + 1, total);
            }
            else
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}'", movie.Title, sourcePath, destinationPath);
            }

            if (sourcePath.PathEquals(destinationPath))
            {
                _logger.ProgressInfo("{0} is already in the specified location '{1}'.", movie, destinationPath);
                return;
            }

            try
            {
                _diskProvider.CreateFolder(new DirectoryInfo(destinationPath).Parent.FullName);

                if (IsSharedAtSource(movie, sourcePath))
                {
                    MoveOwnedFiles(movie, sourcePath, destinationPath);
                }
                else
                {
                    _diskTransferService.TransferFolder(sourcePath, destinationPath, TransferMode.Move);
                }

                _logger.ProgressInfo("{0} moved successfully to {1}", movie.Title, destinationPath);

                _eventAggregator.PublishEvent(new MovieMovedEvent(movie, sourcePath, destinationPath));
            }
            catch (IOException ex)
            {
                _logger.Error(ex, "Unable to move movie from '{0}' to '{1}'. Try moving files manually", sourcePath, destinationPath);

                RevertPath(movie.Id, sourcePath);
            }
        }

        // The movie already carries its new path, so judge sharing against the folder it is leaving.
        private bool IsSharedAtSource(Movie movie, string sourcePath)
        {
            var currentPath = movie.Path;

            movie.Path = sourcePath;

            try
            {
                return _sceneFolderLayout.IsShared(movie);
            }
            finally
            {
                movie.Path = currentPath;
            }
        }

        // A shared folder holds other scenes' files, so only this scene's files and extras move.
        private void MoveOwnedFiles(Movie movie, string sourcePath, string destinationPath)
        {
            var relativePaths = _mediaFileService.GetFilesByMovie(movie.Id).Select(f => f.RelativePath)
                .Concat(_subtitleFileService.GetFilesByMovie(movie.Id).Select(f => f.RelativePath))
                .Concat(_otherExtraFileService.GetFilesByMovie(movie.Id).Select(f => f.RelativePath))
                .Concat(_metadataFileService.GetFilesByMovie(movie.Id).Select(f => f.RelativePath))
                .Where(p => p.IsNotNullOrWhiteSpace())
                .Distinct();

            var plan = relativePaths
                .Select(p => (Source: Path.Combine(sourcePath, p), Destination: Path.Combine(destinationPath, p)))
                .Where(m => _diskProvider.FileExists(m.Source))
                .ToList();

            // The scene's rows are relative to its folder, which is about to change. A file that stays behind would
            // resolve to whatever sits at the same name in the new folder (another scene's file), so any collision
            // aborts the whole move before a single file is touched and the caller reverts the path.
            var collision = plan.FirstOrDefault(m => _diskProvider.FileExists(m.Destination));

            if (collision.Source != null)
            {
                throw new IOException($"Not moving '{collision.Source}': '{collision.Destination}' already exists");
            }

            var moved = new List<(string Source, string Destination)>();

            try
            {
                foreach (var (source, destination) in plan)
                {
                    _diskProvider.CreateFolder(Path.GetDirectoryName(destination));
                    _diskTransferService.TransferFile(source, destination, TransferMode.Move);
                    moved.Add((source, destination));
                }
            }
            catch (Exception)
            {
                RollBack(moved);
                throw;
            }

            if (_diskProvider.FolderExists(sourcePath) && _diskProvider.FolderEmpty(sourcePath))
            {
                _diskProvider.DeleteFolder(sourcePath, true);
            }
        }

        // Put already-moved files back so the reverted path still describes where everything is.
        private void RollBack(List<(string Source, string Destination)> moved)
        {
            foreach (var (source, destination) in Enumerable.Reverse(moved))
            {
                try
                {
                    _diskTransferService.TransferFile(destination, source, TransferMode.Move);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to move '{0}' back to '{1}'", destination, source);
                }
            }
        }

        private void RevertPath(int movieId, string path)
        {
            var movie = _movieService.GetMovie(movieId);

            movie.Path = path;
            _movieService.UpdateMovie(movie);
        }

        public void Execute(MoveMovieCommand message)
        {
            var movie = _movieService.GetMovie(message.MovieId);
            MoveSingleMovie(movie, message.SourcePath, message.DestinationPath);
        }

        public void Execute(BulkMoveMovieCommand message)
        {
            var moviesToMove = message.Movies;
            var destinationRootFolder = message.DestinationRootFolder;

            _logger.ProgressInfo("Moving {0} movies to '{1}'", moviesToMove.Count, destinationRootFolder);

            for (var index = 0; index < moviesToMove.Count; index++)
            {
                var s = moviesToMove[index];
                var movie = _movieService.GetMovie(s.MovieId);
                var destinationPath = Path.Combine(destinationRootFolder, _filenameBuilder.GetMovieFolder(movie));

                MoveSingleMovie(movie, s.SourcePath, destinationPath, index, moviesToMove.Count);
            }

            _logger.ProgressInfo("Finished moving {0} movies to '{1}'", moviesToMove.Count, destinationRootFolder);
        }
    }
}
