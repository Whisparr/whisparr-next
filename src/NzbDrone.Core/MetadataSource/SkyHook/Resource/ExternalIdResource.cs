namespace NzbDrone.Core.MetadataSource.SkyHook.Resource
{
    public class ExternalIdResource
    {
        public string StashId { get; set; }
        public int TmdbId { get; set; }
        public string TpdbId { get; set; }
        public int? TpdbNumericId { get; set; }
    }
}
