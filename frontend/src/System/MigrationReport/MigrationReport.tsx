import React, { useCallback, useMemo } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import MigrationReportModel, {
  MigrationReportCategory,
  MigrationReportItem,
} from 'typings/MigrationReport';
import formatDateTime from 'Utilities/Date/formatDateTime';
import translate from 'Utilities/String/translate';
import useMigrationReport, {
  useDismissMigrationReport,
} from './useMigrationReport';

const CATEGORIES: {
  category: MigrationReportCategory;
  title: () => string;
}[] = [
  {
    category: 'SkippedSetting',
    title: () => translate('MigrationReportSkippedSettings'),
  },
  {
    category: 'DroppedToken',
    title: () => translate('MigrationReportDroppedTokens'),
  },
  {
    category: 'LeftBehind',
    title: () => translate('MigrationReportLeftBehind'),
  },
  {
    category: 'UnmatchedFile',
    title: () => translate('MigrationReportUnmatchedFiles'),
  },
  {
    category: 'NeedsAttention',
    title: () => translate('MigrationReportNeedsAttention'),
  },
];

const columns: Column[] = [
  {
    name: 'area',
    label: () => translate('MigrationReportArea'),
    isVisible: true,
  },
  {
    name: 'subject',
    label: () => translate('MigrationReportSubject'),
    isVisible: true,
  },
  {
    name: 'detail',
    label: () => translate('Details'),
    isVisible: true,
  },
  {
    name: 'count',
    label: () => translate('MigrationReportCount'),
    isVisible: true,
  },
];

const CSV_HEADER = ['Category', 'Area', 'Subject', 'Detail', 'Count'];

// RFC 4180: quote a field holding a comma, quote or line break, doubling quotes.
function escapeCsvField(value: string | number) {
  const text = String(value);

  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

function buildCsv(items: MigrationReportItem[]) {
  const rows = items.map((item) =>
    [item.category, item.area, item.subject, item.detail, item.count]
      .map(escapeCsvField)
      .join(',')
  );

  return `${[CSV_HEADER.join(','), ...rows].join('\r\n')}\r\n`;
}

function downloadCsv(items: MigrationReportItem[]) {
  const url = URL.createObjectURL(
    new Blob([buildCsv(items)], { type: 'text/csv;charset=utf-8' })
  );

  const link = document.createElement('a');
  link.href = url;
  link.download = 'whisparr-migration-report.csv';
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function getSceneSourceLabel(source: string) {
  return source === 'StashDb' ? 'StashDB' : source.toUpperCase();
}

function getMovieSourceLabel(source: string) {
  return source.toUpperCase() === 'NONE' ? translate('None') : source;
}

function getSummary(report: MigrationReportModel) {
  return translate('MigrationReportSummary', {
    legacy: report.sourceKind,
    sceneSource: getSceneSourceLabel(report.sceneSource),
    movieSource: getMovieSourceLabel(report.movieSource),
  });
}

function MigrationReport() {
  const { data: report, hasReport, isLoading } = useMigrationReport();
  const { dismissReport, isDismissing } = useDismissMigrationReport();
  const { longDateFormat, timeFormat, timeZone } = useUiSettingsValues();

  const items = useMemo(() => report?.items ?? [], [report]);

  const handleDownloadPress = useCallback(() => {
    downloadCsv(items);
  }, [items]);

  const handleDismissPress = useCallback(() => {
    dismissReport();
  }, [dismissReport]);

  const formatTimestamp = (value?: string | null) =>
    formatDateTime(value, longDateFormat, timeFormat, { timeZone });

  return (
    <PageContent title={translate('MigrationReport')}>
      <PageContentBody>
        {isLoading ? <LoadingIndicator /> : null}

        {!isLoading && report && hasReport ? (
          <>
            <Alert kind={report.completed ? kinds.INFO : kinds.WARNING}>
              {report.completed
                ? getSummary(report)
                : translate('MigrationReportInProgress')}
            </Alert>

            <dl>
              <dt>{translate('Started')}</dt>
              <dd>{formatTimestamp(report.startedAt)}</dd>

              {report.completed && report.completedAt ? (
                <>
                  <dt>{translate('Completed')}</dt>
                  <dd>{formatTimestamp(report.completedAt)}</dd>
                </>
              ) : null}

              {report.backupPath ? (
                <>
                  <dt>{translate('Backup')}</dt>
                  <dd>{report.backupPath}</dd>
                </>
              ) : null}
            </dl>

            <div>
              <Button onPress={handleDownloadPress}>
                {translate('MigrationReportDownload')}
              </Button>

              <SpinnerButton
                isSpinning={isDismissing}
                isDisabled={!report.completed || report.dismissed}
                onPress={handleDismissPress}
              >
                {translate('MigrationReportDismiss')}
              </SpinnerButton>
            </div>

            {CATEGORIES.map(({ category, title }) => {
              const categoryItems = items.filter(
                (item) => item.category === category
              );

              if (!categoryItems.length) {
                return null;
              }

              return (
                <section key={category}>
                  <h3>{title()}</h3>

                  {category === 'UnmatchedFile' ? (
                    <p>
                      <Link to="/add/import">
                        {translate('MigrationReportManualImport')}
                      </Link>
                    </p>
                  ) : null}

                  <Table columns={columns}>
                    <TableBody>
                      {categoryItems.map((item, index) => (
                        <TableRow key={`${item.area}-${item.subject}-${index}`}>
                          <TableRowCell>{item.area}</TableRowCell>
                          <TableRowCell>{item.subject}</TableRowCell>
                          <TableRowCell>{item.detail}</TableRowCell>
                          <TableRowCell>{item.count}</TableRowCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </section>
              );
            })}
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default MigrationReport;
