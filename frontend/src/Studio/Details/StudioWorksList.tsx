import classNames from 'classnames';
import React, { useCallback, useEffect, useMemo, useRef } from 'react';
import {
  CellMeasurer,
  CellMeasurerCache,
  List,
  type ListImperativeAPI,
  type RowRendererParams,
} from 'react-virtualized';
import { AutoSizer } from 'react-virtualized-auto-sizer';
import { useAppDimension } from 'App/appStore';
import useVisiblePerformerNames from 'Performer/useVisiblePerformerNames';
import StudioDetailsYearHeader from './StudioDetailsYearHeader';
import { useStudioScenesOption } from './studioScenesOptionsStore';
import {
  buildStudioWorksRows,
  StudioWorksRow,
  StudioWorksYear,
} from './studioWorksRows';
import {
  getStudioWorksMinWidth,
  StudioWorksTableFooter,
  StudioWorksTableHeader,
  StudioWorksTableRow,
} from './StudioWorksTable';
import { sortStudioWorks } from './useStudioDetailsYear';
import styles from './StudioWorksTable.module.css';

const SCROLL_LEFT_VAR = '--studio-works-scroll-left';

interface StudioWorksListProps {
  studioId: number;
  worksByYear: StudioWorksYear[];
  expandedState: Record<number, boolean>;
  scrollContainer: Element | null;
  onExpandPress: (year: number, expand: boolean) => void;
  onYearRefreshPress?: (ids: number[]) => void;
}

interface StudioWorksVirtualListProps extends StudioWorksListProps {
  width: number;
}

function getRowClassNames(row: StudioWorksRow) {
  const isCollapsedYear = row.type === 'year' && !row.isExpanded;

  return {
    outer: classNames(
      styles.row,
      (isCollapsedYear || row.type === 'footer') && styles.yearGap
    ),
    card: classNames(
      styles.card,
      row.type === 'year' && styles.cardTop,
      (isCollapsedYear || row.type === 'footer') && styles.cardBottom
    ),
  };
}

function StudioWorksVirtualList({
  studioId,
  worksByYear,
  expandedState,
  scrollContainer,
  width,
  onExpandPress,
  onYearRefreshPress,
}: Readonly<StudioWorksVirtualListProps>) {
  const isSmallScreen = useAppDimension('isSmallScreen');
  const columns = useStudioScenesOption('columns');
  const sortKey = useStudioScenesOption('sortKey');
  const sortDirection = useStudioScenesOption('sortDirection');

  const rows = useMemo(
    () =>
      buildStudioWorksRows(worksByYear, expandedState, (works) =>
        sortStudioWorks(works, sortKey, sortDirection)
      ),
    [worksByYear, expandedState, sortKey, sortDirection]
  );

  // Measured heights are keyed by row, not index, so expanding a year doesn't
  // throw away the heights of the rows below it.
  const rowsRef = useRef(rows);
  rowsRef.current = rows;

  const listRef = useRef<ListImperativeAPI>(null);
  const cacheRef = useRef(
    new CellMeasurerCache({
      fixedWidth: true,
      defaultHeight: 50,
      minHeight: 40,
      keyMapper: (rowIndex) => rowsRef.current[rowIndex]?.key ?? rowIndex,
    })
  );

  const expandedWorks = useMemo(
    () =>
      rows.flatMap((row) =>
        row.type === 'year' && row.isExpanded ? row.works : []
      ),
    [rows]
  );

  // One lookup for every expanded year, instead of one per year
  const { data: visiblePerformerNames } = useVisiblePerformerNames(
    expandedWorks,
    expandedWorks.length > 0
  );

  // Row content wraps with the width and changes with the columns, so heights
  // measured before either change are stale.
  useEffect(() => {
    cacheRef.current.clearAll();
  }, [width, columns, worksByYear]);

  useEffect(() => {
    listRef.current?.recomputeRowHeights();
  }, [rows, width, columns]);

  // When the table is wider than the page and scrolls sideways, year headers
  // and footers keep the visible width and follow the scroll, so their buttons
  // stay on screen. (`position: sticky` can't do it: the list's own boxes
  // clip.)
  const pinnedStyle = useMemo<React.CSSProperties>(
    () => ({ width, transform: `translateX(var(${SCROLL_LEFT_VAR}, 0px))` }),
    [width]
  );

  const rowRenderer = useCallback(
    ({ index, key, parent, style }: RowRendererParams) => {
      const row = rows[index];

      if (!row) {
        return null;
      }

      let content: React.ReactNode = null;

      if (row.type === 'year') {
        content = (
          <StudioDetailsYearHeader
            studioId={studioId}
            year={row.year}
            works={row.works}
            isExpanded={row.isExpanded}
            onExpandPress={onExpandPress}
            onYearRefreshPress={onYearRefreshPress}
          />
        );
      } else if (row.type === 'columns') {
        content = (
          <StudioWorksTableHeader
            columns={columns}
            sortKey={sortKey}
            sortDirection={sortDirection}
          />
        );
      } else if (row.type === 'work') {
        content = (
          <StudioWorksTableRow
            movie={row.movie}
            columns={columns}
            visiblePerformerNames={visiblePerformerNames}
          />
        );
      } else {
        content = (
          <StudioWorksTableFooter
            year={row.year}
            onExpandPress={onExpandPress}
          />
        );
      }

      const rowClassNames = getRowClassNames(row);

      return (
        <CellMeasurer
          key={key}
          cache={cacheRef.current}
          columnIndex={0}
          rowIndex={index}
          parent={parent}
        >
          <div className={rowClassNames.outer} style={style}>
            <div
              className={rowClassNames.card}
              style={
                row.type === 'year' || row.type === 'footer'
                  ? pinnedStyle
                  : undefined
              }
            >
              {content}
            </div>
          </div>
        </CellMeasurer>
      );
    },
    [
      columns,
      pinnedStyle,
      rows,
      sortDirection,
      sortKey,
      studioId,
      visiblePerformerNames,
      onExpandPress,
      onYearRefreshPress,
    ]
  );

  // Below the columns' minimum width the table scrolls sideways instead of
  // squeezing title and performers to nothing. With every year collapsed
  // there is no table to scroll.
  const hasTable = rows.some((row) => row.type === 'columns');
  const listWidth = hasTable
    ? Math.max(width, getStudioWorksMinWidth(columns))
    : width;

  const scrollerStyle = useMemo(() => ({ width }), [width]);

  // Set directly, so a sideways scroll doesn't re-render the list
  const handleHorizontalScroll = useCallback(
    (event: React.UIEvent<HTMLDivElement>) => {
      const scroller = event.currentTarget;
      scroller.style.setProperty(SCROLL_LEFT_VAR, `${scroller.scrollLeft}px`);
    },
    []
  );

  return (
    <div
      className={styles.horizontalScroll}
      style={scrollerStyle}
      onScroll={handleHorizontalScroll}
    >
      <List
        ref={listRef}
        // On small screens the window scrolls, not the page content
        scrollElement={isSmallScreen ? 'window' : scrollContainer}
        width={listWidth}
        rowCount={rows.length}
        rowHeight={cacheRef.current.rowHeight}
        estimatedRowSize={50}
        deferredMeasurementCache={cacheRef.current}
        overscanRowCount={10}
        rowRenderer={rowRenderer}
      />
    </div>
  );
}

function StudioWorksList(props: Readonly<StudioWorksListProps>) {
  const renderList = useCallback(
    ({ width }: { width: number | undefined }) =>
      width ? <StudioWorksVirtualList {...props} width={width} /> : null,
    [props]
  );

  return <AutoSizer renderProp={renderList} />;
}

export default StudioWorksList;
