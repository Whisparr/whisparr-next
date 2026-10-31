using System;
using System.Collections.Generic;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using Whisparr.Http.REST;

namespace Whisparr.Api.V5.UnmappedFiles
{
    public class UnmappedFileResource : RestResource
    {
        public int RootFolderId { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public bool Exists { get; set; }
        public bool Dismissed { get; set; }
    }

    public class UnmappedFileSuggestionResource
    {
        public string ForeignId { get; set; }
        public string Title { get; set; }
        public string StudioTitle { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public ItemType ItemType { get; set; }
        public bool IsExisting { get; set; }
        public bool HasFile { get; set; }
        public bool HashMatch { get; set; }
        public bool SameDate { get; set; }
        public bool SameStudio { get; set; }
        public int TitleSimilarity { get; set; }
    }

    public class UnmappedFileSuggestionsResource
    {
        public string Term { get; set; }
        public List<UnmappedFileSuggestionResource> Results { get; set; }
    }

    public class UnmappedFileLinkResource
    {
        public string ForeignId { get; set; }
        public ItemType ItemType { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }
    }

    public class UnmappedFileDismissResource
    {
        public List<int> Ids { get; set; }
        public bool Dismissed { get; set; }
    }

    public static class UnmappedFileResourceMapper
    {
        public static UnmappedFileSuggestionResource ToResource(this UnmappedFileSuggestion model, Movie existing)
        {
            return new UnmappedFileSuggestionResource
            {
                ForeignId = model.Movie.ForeignId,
                Title = model.Movie.Title,
                StudioTitle = model.Movie.MovieMetadata.Value.StudioTitle,
                ReleaseDate = model.Movie.GetReleaseDate(),
                ItemType = model.Movie.MovieMetadata.Value.ItemType,
                IsExisting = existing != null,
                HasFile = existing?.HasFile ?? false,
                HashMatch = model.HashMatch,
                SameDate = model.SameDate,
                SameStudio = model.SameStudio,
                TitleSimilarity = model.TitleSimilarity
            };
        }
    }
}
