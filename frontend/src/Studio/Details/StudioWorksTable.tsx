import React, { useCallback } from 'react';
import IconButton from 'Components/Link/IconButton';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import { icons } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import Movie from 'Movie/Movie';
import SceneRow from 'Performer/Details/SceneRow';
import translate from 'Utilities/String/translate';
import {
  changeStudioScenesTableOptions,
  sortStudioScenes,
} from './useStudioDetailsYear';
import styles from './StudioWorksTable.module.css';

// The Works table is rendered as one small table per virtualized row, so every
// row gets the same fixed column widths to line up. Columns left out here
// share the remaining width.
const FIXED_COLUMN_WIDTHS: Record<string, number> = {
  monitored: 42,
  releaseDate: 130,
  runtime: 80,
  sizeOnDisk: 100,
  status: 120,
  actions: 70,
};

const MIN_FLEXIBLE_COLUMN_WIDTH = 180;

export function getStudioWorksMinWidth(columns: readonly Column[]) {
  return columns.reduce((total, { name, isVisible }) => {
    if (!isVisible) {
      return total;
    }

    return total + (FIXED_COLUMN_WIDTHS[name] ?? MIN_FLEXIBLE_COLUMN_WIDTH);
  }, 0);
}

function StudioWorksColGroup({ columns }: Readonly<{ columns: Column[] }>) {
  return (
    <colgroup>
      {columns
        .filter((column) => column.isVisible)
        .map(({ name }) => (
          <col key={name} style={{ width: FIXED_COLUMN_WIDTHS[name] }} />
        ))}
    </colgroup>
  );
}

interface StudioWorksTableHeaderProps {
  columns: Column[];
  sortKey: string;
  sortDirection: SortDirection;
}

export function StudioWorksTableHeader({
  columns,
  sortKey,
  sortDirection,
}: Readonly<StudioWorksTableHeaderProps>) {
  const handleSortPress = useCallback(
    (name: string, direction?: SortDirection) => {
      sortStudioScenes(name, direction, sortKey, sortDirection);
    },
    [sortKey, sortDirection]
  );

  return (
    <Table
      className={styles.table}
      horizontalScroll={false}
      columns={columns}
      colGroup={<StudioWorksColGroup columns={columns} />}
      sortKey={sortKey}
      sortDirection={sortDirection}
      onSortPress={handleSortPress}
      onTableOptionChange={changeStudioScenesTableOptions}
    />
  );
}

interface StudioWorksTableRowProps {
  movie: Movie;
  columns: Column[];
  visiblePerformerNames?: Record<string, string[]>;
}

export function StudioWorksTableRow({
  movie,
  columns,
  visiblePerformerNames,
}: Readonly<StudioWorksTableRowProps>) {
  return (
    <table className={styles.table}>
      <StudioWorksColGroup columns={columns} />
      <tbody>
        <SceneRow
          movie={movie}
          columns={columns}
          visiblePerformerNames={visiblePerformerNames}
        />
      </tbody>
    </table>
  );
}

interface StudioWorksTableFooterProps {
  year: number;
  onExpandPress: (year: number, expand: boolean) => void;
}

export function StudioWorksTableFooter({
  year,
  onExpandPress,
}: Readonly<StudioWorksTableFooterProps>) {
  const handleCollapsePress = useCallback(() => {
    onExpandPress(year, false);
  }, [onExpandPress, year]);

  return (
    <div className={styles.collapseButtonContainer}>
      <IconButton
        iconClassName={styles.collapseButtonIcon}
        name={icons.COLLAPSE}
        size={20}
        title={translate('HideMovies')}
        onPress={handleCollapsePress}
      />
    </div>
  );
}
