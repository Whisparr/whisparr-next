using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.LegacyMigration
{
    public enum LegacyReportCategory
    {
        SkippedSetting = 0,
        DroppedToken = 1,
        LeftBehind = 2,
        UnmatchedFile = 3,
        NeedsAttention = 4
    }

    // One line of the migration report: something the porter skipped, dropped or could not match.
    public class LegacyMigrationReportItem : ModelBase
    {
        public LegacyReportCategory Category { get; set; }
        public string Area { get; set; }
        public string Subject { get; set; }
        public string Detail { get; set; }
        public int Count { get; set; }
    }
}
