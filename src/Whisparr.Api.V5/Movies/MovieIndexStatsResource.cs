namespace Whisparr.Api.V5.Movies
{
    public class MovieIndexStatsResource
    {
        public int TotalCount { get; set; }
        public int MonitoredCount { get; set; }
        public int MovieFiles { get; set; }
        public long TotalFileSize { get; set; }
    }
}
