using System;
using System.Collections.Generic;

namespace Whisparr.Api.V5.System.MigrationReport
{
    public class MigrationReportResource
    {
        public string SourceKind { get; set; }
        public string SceneSource { get; set; }
        public string MovieSource { get; set; }
        public bool Completed { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string BackupPath { get; set; }
        public bool Dismissed { get; set; }
        public List<MigrationReportItemResource> Items { get; set; }
    }

    public class MigrationReportItemResource
    {
        public string Category { get; set; }
        public string Area { get; set; }
        public string Subject { get; set; }
        public string Detail { get; set; }
        public int Count { get; set; }
    }
}
