using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using Whisparr.Http;
using Whisparr.Http.REST;
using NzbDroneClientException = NzbDrone.Core.Exceptions.NzbDroneClientException;

namespace Whisparr.Api.V5.Config
{
    [V5ApiController("config/metadatasource")]
    public class MetadataSourceController : Controller
    {
        private readonly IMetadataSourceService _metadataSourceService;

        public MetadataSourceController(IMetadataSourceService metadataSourceService)
        {
            _metadataSourceService = metadataSourceService;
        }

        [HttpGet]
        [Produces("application/json")]
        public MetadataSourceResource GetMetadataSources()
        {
            return _metadataSourceService.Get().ToResource();
        }

        [HttpPut]
        [Consumes("application/json")]
        [Produces("application/json")]
        public MetadataSourceResource ChooseMetadataSources([FromBody] MetadataSourceResource resource)
        {
            if (resource == null)
            {
                throw new BadRequestException("Request body can't be empty");
            }

            // The choice locks permanently, so anything missing or out of range is refused up front.
            if (resource.SceneMetadataSource is not { } scene || !Enum.IsDefined(scene))
            {
                throw new BadRequestException("A valid scene metadata source is required");
            }

            if (resource.MovieMetadataSource is not { } movie || !Enum.IsDefined(movie) || movie == MovieMetadataType.NONE)
            {
                throw new BadRequestException("A movie metadata source is required");
            }

            try
            {
                _metadataSourceService.Choose(scene, movie);
            }
            catch (MetadataSourcesLockedException ex)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, ex.Message);
            }

            return _metadataSourceService.Get().ToResource();
        }
    }
}
