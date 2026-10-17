namespace Whisparr.Api.V5.Movies
{
    public class MoviePatchResource
    {
        // Initially for toggle monitored performance, but can be extended
        // to support other patch operations in the future
        public bool Monitored { get; set; }
    }
}
