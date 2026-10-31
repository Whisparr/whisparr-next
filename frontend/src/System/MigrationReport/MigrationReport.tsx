import React, { useCallback, useMemo } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
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
import { icons, kinds } from 'Helpers/Props';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import MigrationReportModel, {
  MigrationReportCategory,
  MigrationReportItem,
} from 'typings/MigrationReport';
import formatDateTime from 'Utilities/Date/formatDateTime';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import translate from 'Utilities/String/translate';
import useMigrationReport, {
  useDismissMigrationReport,
  useRemoveMigrationReportItem,
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
  {
    name: 'actions',
    label: '',
    isVisible: true,
  },
];

const CSV_HEADER = ['Category', 'Area', 'Subject', 'Detail', 'Count'];

// RFC 4180: quote a field holding a comma, quote or line break, doubling quotes.
function escapeCsvField(value: string | number | null) {
  const text = value === null ? '' : String(value);

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

function RequestError({ error }: Readonly<{ error: ApiError | null }>) {
  return error ? (
    <Alert kind={kinds.DANGER}>
      {error.statusBody?.message ?? error.message}
    </Alert>
  ) : null;
}

const SEARCH_ROUTES: Record<string, string> = {
  Studios: '/add/new/studio',
  Performers: '/add/new/performer',
};

// Only rows the source couldn't match are worth searching for, not merges or
// errors. The server words them "... not found on X" or "... has this name".
function getSearchLink(item: MigrationReportItem) {
  const route = SEARCH_ROUTES[item.area];

  if (
    !route ||
    !item.subject ||
    !/not found on|has this name/.test(item.detail ?? '')
  ) {
    return null;
  }

  return `${route}?term=${encodeURIComponent(item.subject)}`;
}

interface ReportRowProps {
  item: MigrationReportItem;
  canRemove: boolean;
  onRemovePress: (id: number) => void;
}

function ReportRow({
  item,
  canRemove,
  onRemovePress,
}: Readonly<ReportRowProps>) {
  const handleRemovePress = useCallback(() => {
    onRemovePress(item.id);
  }, [item.id, onRemovePress]);

  const searchLink = getSearchLink(item);

  return (
    <TableRow>
      <TableRowCell>{item.area}</TableRowCell>
      <TableRowCell>{item.subject}</TableRowCell>
      <TableRowCell>{item.detail}</TableRowCell>
      <TableRowCell>{item.count}</TableRowCell>
      <TableRowCell>
        {searchLink ? (
          <IconButton
            name={icons.SEARCH}
            title={translate('Search')}
            to={searchLink}
          />
        ) : null}

        <IconButton
          name={icons.REMOVE}
          title={translate('MigrationReportRemoveItem')}
          isDisabled={!canRemove}
          onPress={handleRemovePress}
        />
      </TableRowCell>
    </TableRow>
  );
}

interface CategorySectionProps {
  category: MigrationReportCategory;
  title: string;
  items: MigrationReportItem[];
  canRemove: boolean;
  onRemovePress: (id: number) => void;
}

// An empty category is hidden.
function CategorySection({
  category,
  title,
  items,
  canRemove,
  onRemovePress,
}: Readonly<CategorySectionProps>) {
  if (!items.length) {
    return null;
  }

  return (
    <section>
      <h3>{title}</h3>

      {category === 'UnmatchedFile' ? (
        <p>
          <Link to="/add/import">
            {translate('MigrationReportManualImport')}
          </Link>
        </p>
      ) : null}

      <Table columns={columns}>
        <TableBody>
          {items.map((item) => (
            <ReportRow
              key={item.id}
              item={item}
              canRemove={canRemove}
              onRemovePress={onRemovePress}
            />
          ))}
        </TableBody>
      </Table>
    </section>
  );
}

function MigrationReport() {
  const { data: report, hasReport, isLoading } = useMigrationReport();
  const { dismissReport, isDismissing, dismissError } =
    useDismissMigrationReport();
  const { removeItem, isRemoving, removeError } =
    useRemoveMigrationReportItem();
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

        {!isLoading && !hasReport ? (
          <Alert kind={kinds.INFO}>{translate('MigrationReportNone')}</Alert>
        ) : null}

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

            <RequestError error={dismissError} />
            <RequestError error={removeError} />

            {CATEGORIES.map(({ category, title }) => {
              const categoryItems = items.filter(
                (item) => item.category === category
              );

              return (
                <CategorySection
                  key={category}
                  category={category}
                  title={title()}
                  items={categoryItems}
                  canRemove={report.completed && !isRemoving}
                  onRemovePress={removeItem}
                />
              );
            })}
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default MigrationReport;
