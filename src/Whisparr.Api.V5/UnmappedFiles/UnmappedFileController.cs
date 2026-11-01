using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using Whisparr.Http;
using Whisparr.Http.Extensions;
using Whisparr.Http.REST;

namespace Whisparr.Api.V5.UnmappedFiles
{
    [V5ApiController]
    public class UnmappedFileController : Controller
    {
        private readonly IUnmappedFileService _unmappedFileService;
        private readonly IMovieService _movieService;
        private readonly IDiskProvider _diskProvider;

        public UnmappedFileController(IUnmappedFileService unmappedFileService,
                                      IMovieService movieService,
                                      IDiskProvider diskProvider)
        {
            _unmappedFileService = unmappedFileService;
            _movieService = movieService;
            _diskProvider = diskProvider;
        }

        [HttpGet]
        [Produces("application/json")]
        public PagingResource<UnmappedFileResource> GetUnmappedFiles([FromQuery] PagingRequestResource paging, [FromQuery] string term = null, [FromQuery] bool dismissed = false)
        {
            var pagingResource = new PagingResource<UnmappedFileResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<UnmappedFileResource, ImportFile>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "path" },
                "path",
                SortDirection.Ascending);

            pagingSpec.FilterExpressions.Add(f => f.Dismissed == dismissed);

            if (term.IsNotNullOrWhiteSpace())
            {
                pagingSpec.FilterExpressions.Add(f => f.Path.Contains(term));
            }

            return pagingSpec.ApplyToPage(_unmappedFileService.Paged, f => f.ToResource(_diskProvider.FileExists(f.Path)));
        }

        [HttpGet("{id:int}/suggestions")]
        [Produces("application/json")]
        public UnmappedFileSuggestionsResource GetSuggestions([FromRoute] int id, [FromQuery] string term = null, [FromQuery] ItemType itemType = ItemType.Scene)
        {
            var suggestions = _unmappedFileService.Suggest(id, term, itemType);
            var existing = _movieService.FindByForeignIds(suggestions.Results.Select(s => s.Movie.ForeignId).ToList())
                .GroupBy(m => m.ForeignId)
                .ToDictionary(g => g.Key, g => g.First());

            return new UnmappedFileSuggestionsResource
            {
                Term = suggestions.Term,
                Results = suggestions.Results.Select(s => s.ToResource(existing.GetValueOrDefault(s.Movie.ForeignId))).ToList()
            };
        }

        [HttpPost("{id:int}/link")]
        [Consumes("application/json")]
        public object Link([FromRoute] int id, [FromBody] UnmappedFileLinkResource resource)
        {
            if (resource.ForeignId.IsNullOrWhiteSpace())
            {
                throw new BadRequestException("foreignId must be provided");
            }

            var movie = _unmappedFileService.Link(id, resource.ForeignId, resource.ItemType, resource.QualityProfileId, resource.Monitored);

            return new { movie.Id, movie.ForeignId };
        }

        [HttpPut("dismiss")]
        [Consumes("application/json")]
        public object Dismiss([FromBody] UnmappedFileDismissResource resource)
        {
            if (resource.Ids == null || resource.Ids.Count == 0)
            {
                throw new BadRequestException("ids must be provided");
            }

            _unmappedFileService.SetDismissed(resource.Ids, resource.Dismissed);

            return new { };
        }
    }
}
