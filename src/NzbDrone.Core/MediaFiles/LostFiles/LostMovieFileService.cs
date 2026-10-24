using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.LostFiles
{
    public interface ILostMovieFileService
    {
        // True when at least one scene file is remembered, so a scan can skip hashing candidates when none is
        bool Any();

        void Record(Movie movie, MovieFile file);

        // The file at path, re-linked in place to the one scene that lost a file with this oshash and size, or null
        MovieFile TryRelink(string path, string osHash, long size);
    }

    public class LostMovieFileService :
        ILostMovieFileService,
        IHandle<MoviesDeletedEvent>,
        IHandle<MovieFileImportedEvent>
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

        private readonly ILostMovieFileRepository _repository;
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedMovie _importApprovedMovies;
        private readonly IRootFolderService _rootFolderService;
        private readonly Logger _logger;

        public LostMovieFileService(ILostMovieFileRepository repository,
                                    IMovieService movieService,
                                    IMediaFileService mediaFileService,
                                    IMakeImportDecision importDecisionMaker,
                                    IImportApprovedMovie importApprovedMovies,
                                    IRootFolderService rootFolderService,
                                    Logger logger)
        {
            _repository = repository;
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedMovies = importApprovedMovies;
            _rootFolderService = rootFolderService;
            _logger = logger;
        }

        public bool Any()
        {
            return _repository.Count() > 0;
        }

        public void Record(Movie movie, MovieFile file)
        {
            if (file.OsHash.IsNullOrWhiteSpace())
            {
                return;
            }

            _repository.Insert(new LostMovieFile
            {
                MovieId = movie.Id,
                OsHash = file.OsHash,
                Size = file.Size,
                RelativePath = file.RelativePath,
                LostAt = DateTime.UtcNow
            });

            _logger.Debug("Remembering lost file {0} of {1} for {2} days", file.RelativePath, movie, Lifetime.Days);
        }

        public MovieFile TryRelink(string path, string osHash, long size)
        {
            var cutoff = DateTime.UtcNow - Lifetime;
            var entries = _repository.FindByHash(osHash, size).Where(e => e.LostAt >= cutoff).ToList();

            // Two scenes (or two files) that look the same make the match a guess, so nothing is re-linked
            if (entries.Count != 1)
            {
                return null;
            }

            var entry = entries[0];

            if (_mediaFileService.GetFilesByMovie(entry.MovieId).Any())
            {
                return null;
            }

            var movie = _movieService.GetMovie(entry.MovieId);

            // Re-linking across root folders is out of scope
            if (!string.Equals(_rootFolderService.GetBestRootFolderPath(movie.Path), _rootFolderService.GetBestRootFolderPath(path), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var folder = path.GetParentPath();

            // Defence in depth: a file any item already tracks is never handed to a second scene
            if (_mediaFileService.FilterFilesTrackedByAnyItem(folder, new List<string> { path }).Count == 0)
            {
                return null;
            }

            var previousPath = movie.Path;

            // The lost row kept no quality or media info, so the file goes through the normal existing-file import
            // for this one scene, which fills both. It runs as not-a-new-download: nothing is moved or copied.
            // The new path is saved first: handlers fired during the import may re-read the movie from the DB.
            movie.Path = folder;
            _movieService.UpdateMovie(movie);

            bool imported;

            try
            {
                var decisions = _importDecisionMaker.GetImportDecisions(new List<string> { path }, movie, false);
                var results = _importApprovedMovies.Import(decisions, false);

                imported = results.Any(r => r.Result == ImportResultType.Imported);
            }
            catch
            {
                RestorePath(movie, previousPath);

                throw;
            }

            if (!imported)
            {
                RestorePath(movie, previousPath);

                return null;
            }

            _repository.Delete(entry);

            _logger.Info("Re-linked {0} to {1} by oshash; it was {2} before it went missing", path, movie, entry.RelativePath);

            return _mediaFileService.GetFilesByMovie(movie.Id).FirstOrDefault();
        }

        private void RestorePath(Movie movie, string previousPath)
        {
            movie.Path = previousPath;
            _movieService.UpdateMovie(movie);
        }

        public void Handle(MoviesDeletedEvent message)
        {
            _repository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }

        public void Handle(MovieFileImportedEvent message)
        {
            _repository.DeleteForMovies(new List<int> { message.ImportedMovie.MovieId });
        }
    }
}
