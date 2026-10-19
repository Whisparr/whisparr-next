using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    public enum ScenePageStatus
    {
        Complete,
        Pending,
        Throttled,
        Building
    }

    // Ids are in the locked scene source: StashDB ids or TPDB UUIDs.
    public class ScenePageItem
    {
        public string SceneId { get; set; }
        public List<string> PerformerIds { get; set; }
    }

    public class ScenePage
    {
        public ScenePageStatus Status { get; set; }
        public int Total { get; set; }
        public string NextCursor { get; set; }
        public List<ScenePageItem> Items { get; set; } = new();
        public List<string> Pending { get; set; } = new();
        public TimeSpan RetryAfter { get; set; }
    }
}
