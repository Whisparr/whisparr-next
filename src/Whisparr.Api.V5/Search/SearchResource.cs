using Whisparr.Api.V5.Movies;
using Whisparr.Api.V5.Performers;
using Whisparr.Http.REST;

namespace Whisparr.Api.V5.Search
{
    public class
    SearchResource : RestResource
    {
        public string ForeignId { get; set; }
        public MovieResource Movie { get; set; }
        public PerformerResource Performer { get; set; }
        public Studios.StudioResource Studio { get; set; }
        public bool isExisting { get; set; }
    }
}
