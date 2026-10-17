using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace Whisparr.Http
{
    public class ApiInfoController : Controller
    {
        [HttpGet("/api")]
        [Produces("application/json")]
        public ApiInfoResource GetApiInfo()
        {
            return new ApiInfoResource
            {
                Current = "v5",
                Deprecated = new List<string> { "v3" }
            };
        }
    }
}
