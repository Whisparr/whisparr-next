export type MigrationReportCategory =
  | 'SkippedSetting'
  | 'DroppedToken'
  | 'LeftBehind'
  | 'UnmatchedFile'
  | 'NeedsAttention';

export interface MigrationReportItem {
  category: MigrationReportCategory;
  area: string;
  subject: string;
  detail: string;
  count: number;
}

interface MigrationReport {
  sourceKind: string;
  sceneSource: string;
  movieSource: string;
  completed: boolean;
  startedAt: string;
  completedAt: string | null;
  backupPath: string | null;
  dismissed: boolean;
  items: MigrationReportItem[];
}

export default MigrationReport;
