using System.Collections.Generic;
using Whisparr.Api.V5.Movies;
using Whisparr.Api.V5.Performers;
using Whisparr.Api.V5.Studios;

namespace Whisparr.Api.V5.LibrarySearch
{
    public class LibrarySearchSectionResource<TResource>
    {
        public int TotalRecords { get; set; }
        public List<TResource> Records { get; init; } = new();
    }

    public class LibrarySearchResource
    {
        public LibrarySearchSectionResource<PerformerResource> Performers { get; set; } = new();
        public LibrarySearchSectionResource<StudioResource> Studios { get; set; } = new();
        public LibrarySearchSectionResource<MovieResource> Scenes { get; set; } = new();
        public LibrarySearchSectionResource<MovieResource> Movies { get; set; } = new();
    }
}
