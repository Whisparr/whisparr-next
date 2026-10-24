using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaFiles;
using Whisparr.Http.REST;

namespace Whisparr.Api.V5.Movies
{
    public class RenameMovieResource : RestResource
    {
        public int MovieId { get; set; }
        public int MovieFileId { get; set; }
        public string ExistingPath { get; set; }
        public string NewPath { get; set; }
    }

    public static class RenameMovieResourceMapper
    {
        public static RenameMovieResource ToResource(this RenameMovieFilePreview model)
        {
            if (model == null)
            {
                return null;
            }

            return new RenameMovieResource
            {
                MovieId = model.MovieId,
                MovieFileId = model.MovieFileId,

                // Root-relative when the folder changes, so the preview shows the move
                ExistingPath = model.ExistingRootRelativePath ?? model.ExistingPath,
                NewPath = model.NewRootRelativePath ?? model.NewPath
            };
        }

        public static List<RenameMovieResource> ToResource(this IEnumerable<RenameMovieFilePreview> models)
        {
            return models.Select(ToResource).ToList();
        }
    }
}
