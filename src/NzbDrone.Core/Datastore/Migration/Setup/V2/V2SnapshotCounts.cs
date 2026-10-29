namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    // Row counts of the v2 library tables. Filled in by the library snapshot (Task 6).
    public class V2SnapshotCounts
    {
        public int Series { get; set; }
        public int Episodes { get; set; }
        public int EpisodeFiles { get; set; }
    }
}
