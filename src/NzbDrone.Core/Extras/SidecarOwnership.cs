using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Extras
{
    public static class SidecarName
    {
        // True when the sidecar's name starts with the video's base name, followed by a boundary
        // ('.', '-' or the end of the name), so "Title.en.srt" matches "Title" but "Title 2.nfo" doesn't.
        public static bool SidecarNameMatches(string sidecarFileName, string videoBaseName)
        {
            if (string.IsNullOrEmpty(sidecarFileName) || string.IsNullOrEmpty(videoBaseName))
            {
                return false;
            }

            var name = Path.GetFileName(sidecarFileName);

            if (!name.StartsWith(videoBaseName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (name.Length == videoBaseName.Length)
            {
                return true;
            }

            var next = name[videoBaseName.Length];

            return next == '.' || next == '-';
        }
    }

    public interface ISidecarOwnership
    {
        MovieFile FindOwner(Movie movie, string sidecarPath, List<MovieFile> folderFiles);
        List<MovieFile> GetFolderFiles(Movie movie);
    }

    public class SidecarOwnership : ISidecarOwnership
    {
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly ISceneFolderLayout _sceneFolderLayout;

        public SidecarOwnership(IMovieService movieService,
                                IMediaFileService mediaFileService,
                                ISceneFolderLayout sceneFolderLayout)
        {
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _sceneFolderLayout = sceneFolderLayout;
        }

        // Every tracked video file of every item in the movie's folder.
        public List<MovieFile> GetFolderFiles(Movie movie)
        {
            return _sceneFolderLayout.GetItemsInFolder(movie.Path)
                                     .SelectMany(e => _mediaFileService.GetFilesByMovie(e.Id))
                                     .ToList();
        }

        public MovieFile FindOwner(Movie movie, string sidecarPath, List<MovieFile> folderFiles)
        {
            // v2's way: the sidecar's own name identifies the item.
            // Only the file name is parsed; ParseMinimalPathMovieInfo falls back to the folder name,
            // which in a shared folder would resolve a generic name like movie.nfo to a scene.
            var parsed = Parser.Parser.ParseMovieTitle(Path.GetFileName(sidecarPath), true);

            if (parsed != null)
            {
                var scene = _movieService.FindScene(parsed);

                if (scene != null)
                {
                    return scene.Id == movie.Id ? folderFiles.FirstOrDefault(f => f.MovieId == movie.Id) : null;
                }
            }

            // Otherwise the longest video base name that matches at a boundary wins.
            var matches = folderFiles
                .Where(f => !string.IsNullOrEmpty(f.RelativePath))
                .Select(f => (File: f, BaseName: Path.GetFileNameWithoutExtension(f.RelativePath)))
                .Where(m => SidecarName.SidecarNameMatches(sidecarPath, m.BaseName))
                .ToList();

            if (matches.Count == 0)
            {
                return null;
            }

            var longest = matches.Max(m => m.BaseName.Length);
            var winners = matches.Where(m => m.BaseName.Length == longest).Select(m => m.File).ToList();

            if (winners.Select(f => f.MovieId).Distinct().Count() > 1)
            {
                return null;
            }

            return winners[0].MovieId == movie.Id ? winners[0] : null;
        }
    }
}
