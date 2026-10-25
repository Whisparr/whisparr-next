using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Extras.Metadata.Consumers.Kometa
{
    public class KometaMetadata : MetadataBase<KometaMetadataSettings>
    {
        private static readonly Regex MovieImagesRegex = new (@"^(?:poster|background)\.(?:png|jpe?g)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly ISceneFolderLayout _sceneFolderLayout;

        public override string Name => "Kometa";

        public override bool SupportsPerFileImages => false;

        public KometaMetadata(IMapCoversToLocal mediaCoverService, ISceneFolderLayout sceneFolderLayout)
        {
            _mediaCoverService = mediaCoverService;
            _sceneFolderLayout = sceneFolderLayout;
        }

        public override MetadataFile FindMetadataFile(Movie movie, string path)
        {
            var filename = Path.GetFileName(path);

            if (filename == null)
            {
                return null;
            }

            var metadata = new MetadataFile
            {
                MovieId = movie.Id,
                Consumer = GetType().Name,
                RelativePath = movie.Path.GetRelativePath(path)
            };

            if (MovieImagesRegex.IsMatch(filename))
            {
                metadata.Type = MetadataType.MovieImage;
                return metadata;
            }

            return null;
        }

        public override MetadataFileResult MovieMetadata(Movie movie, MovieFile movieFile)
        {
            return null;
        }

        public override List<ImageFileResult> MovieImages(Movie movie, MovieFile movieFile)
        {
            // Skipped in a shared folder: poster.jpg/background.jpg are folder-level and Kometa has no per-file form
            if (!Settings.MovieImages || _sceneFolderLayout.IsShared(movie))
            {
                return new List<ImageFileResult>();
            }

            return ProcessMovieImages(movie).ToList();
        }

        private IEnumerable<ImageFileResult> ProcessMovieImages(Movie movie)
        {
            foreach (var image in movie.MovieMetadata.Value.Images.Where(i => i.CoverType is MediaCoverTypes.Poster or MediaCoverTypes.Fanart))
            {
                var source = _mediaCoverService.GetMovieCoverPath(movie.Id, image.CoverType);

                var filename = image.CoverType switch
                {
                    MediaCoverTypes.Poster => "poster",
                    MediaCoverTypes.Fanart => "background",
                    _ => throw new ArgumentOutOfRangeException($"{image.CoverType} is not supported")
                };

                var destination = filename + Path.GetExtension(source);

                yield return new ImageFileResult(destination, source);
            }
        }
    }
}
