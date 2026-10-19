using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.SkyHook.Resource
{
    public class ScenePageResource
    {
        public int Total { get; set; }
        public string NextCursor { get; set; }
        public List<MovieResource> Items { get; set; }
        public List<string> Pending { get; set; }

        // A 202 while Skyhook first builds an owner's catalogue: no items and no cursor yet.
        public bool Building { get; set; }
    }

    public class PerformerBulkResource
    {
        public List<PerformerResource> Performers { get; set; }
        public List<string> Pending { get; set; }
    }
}
