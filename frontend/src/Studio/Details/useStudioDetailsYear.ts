import { orderBy } from 'lodash';
import { useCallback } from 'react';
import * as commandNames from 'Commands/commandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import { sortDirections } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import Movie from 'Movie/Movie';
import { useBulkMonitorMovies } from 'Movie/useMovie';
import { TableOptionsChangePayload } from 'typings/Table';
import {
  SECONDARY_SORT_DIRECTION,
  SECONDARY_SORT_KEY,
  setStudioScenesColumns,
  setStudioScenesSort,
} from './studioScenesOptionsStore';

interface StudioDetailsYearActions {
  onMonitorYearPress: () => void;
  onSearchPress: () => void;
}

function getSortClause(sortKey: string) {
  return function (item: Movie) {
    return item[sortKey as keyof Movie];
  };
}

export function sortStudioWorks(
  items: Movie[],
  sortKey: string,
  sortDirection: SortDirection
): Movie[] {
  const clauses = [getSortClause(sortKey)];
  const orders: Array<'asc' | 'desc'> = [
    sortDirection === sortDirections.ASCENDING ? 'asc' : 'desc',
  ];

  if (
    sortKey !== SECONDARY_SORT_KEY ||
    sortDirection !== SECONDARY_SORT_DIRECTION
  ) {
    clauses.push(getSortClause(SECONDARY_SORT_KEY));
    orders.push(
      SECONDARY_SORT_DIRECTION === sortDirections.ASCENDING ? 'asc' : 'desc'
    );
  }

  return orderBy(items, clauses, orders);
}

export function changeStudioScenesTableOptions({
  columns,
}: TableOptionsChangePayload) {
  // This table has no page size, so the modal only ever sends columns.
  if (columns) {
    setStudioScenesColumns(columns);
  }
}

export function sortStudioScenes(
  sortKey: string,
  sortDirection: SortDirection | undefined,
  currentSortKey: string,
  currentSortDirection: SortDirection
) {
  let nextDirection = sortDirection;

  if (!nextDirection) {
    if (sortKey === currentSortKey) {
      nextDirection =
        currentSortDirection === sortDirections.ASCENDING
          ? sortDirections.DESCENDING
          : sortDirections.ASCENDING;
    } else {
      nextDirection = sortDirections.ASCENDING;
    }
  }

  setStudioScenesSort(sortKey, nextDirection);
}

export function useStudioDetailsYearActions(
  studioId: number,
  year: number,
  items: Movie[]
): StudioDetailsYearActions {
  const executeCommand = useExecuteCommand();

  const monitorMovies = useBulkMonitorMovies(true);
  const unmonitorMovies = useBulkMonitorMovies(false);

  const onMonitorYearPress = useCallback(() => {
    const allMonitored = items.every((movie) => movie.monitored);
    const mutation = allMonitored ? unmonitorMovies : monitorMovies;

    mutation.mutate(items.map((item) => item.id));
  }, [items, monitorMovies, unmonitorMovies]);

  const onSearchPress = useCallback(() => {
    executeCommand({
      name: commandNames.STUDIO_SEARCH,
      studioIds: [studioId],
      years: [year],
    });
  }, [studioId, year, executeCommand]);

  return {
    onMonitorYearPress,
    onSearchPress,
  };
}
