using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using Whisparr.Http;

namespace Whisparr.Api.V5.Movies;

[V5ApiController("movie")]
public class MovieFolderController : Controller
{
    private readonly IMovieService _movieService;
    private readonly IBuildFileNames _fileNameBuilder;

    public MovieFolderController(IMovieService movieService, IBuildFileNames fileNameBuilder)
    {
        _movieService = movieService;
        _fileNameBuilder = fileNameBuilder;
    }

    [HttpGet("{id}/folder")]
    [Produces("application/json")]
    public MovieFolderResource GetFolder([FromRoute] int id)
    {
        var movie = _movieService.GetMovie(id);
        var folder = _fileNameBuilder.GetMovieFolder(movie);

        return new MovieFolderResource
        {
            Folder = folder
        };
    }
}
