export type MigrationReportCategory =
  | 'SkippedSetting'
  | 'DroppedToken'
  | 'LeftBehind'
  | 'UnmatchedFile'
  | 'NeedsAttention';

export interface MigrationReportItem {
  id: number;
  category: MigrationReportCategory;
  area: string;
  subject: string | null;
  detail: string | null;
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
