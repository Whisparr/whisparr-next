import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import * as commandNames from 'Commands/commandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import TablePager from 'Components/Table/TablePager';
import { Filter as AppStateFilter } from 'Filters/Filter';
import { useCustomFiltersList } from 'Filters/useCustomFilters';
import useSelectState from 'Helpers/Hooks/useSelectState';
import { align, icons, kinds } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import { CheckInputChanged } from 'typings/inputs';
import { SelectStateInputProps } from 'typings/props';
import { TableOptionsChangePayload } from 'typings/Table';
import translate from 'Utilities/String/translate';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import QueueFilterModal from './QueueFilterModal';
import QueueOptions from './QueueOptions';
import {
  setQueueOption,
  setQueueOptions,
  setQueueSort,
  useQueueOptions,
} from './queueOptionsStore';
import { forceRefreshQueue, setQueueRefreshBlocked } from './queueRefresh';
import QueueRow from './QueueRow';
import RemoveQueueItemModal, { RemovePressProps } from './RemoveQueueItemModal';
import useQueueStatus from './Status/useQueueStatus';
import useQueue, {
  FILTERS,
  useGrabQueueItems,
  useRemoveQueueItems,
} from './useQueue';

function Queue() {
  const executeCommand = useExecuteCommand();

  const { columns, selectedFilterKey, sortKey, sortDirection, pageSize } =
    useQueueOptions();

  const {
    records: items,
    totalPages,
    totalRecords,
    isFetching,
    isLoading,
    isFetched,
    isPlaceholderData,
    error,
    page,
    goToPage,
  } = useQueue();

  const { grabQueueItems, isGrabbing } = useGrabQueueItems();
  const { removeQueueItems, isRemoving } = useRemoveQueueItems();

  const { count } = useQueueStatus();
  const customFilters = useCustomFiltersList('queue');

  const isRefreshMonitoredDownloadsExecuting = useCommandExecuting(
    commandNames.REFRESH_MONITORED_DOWNLOADS
  );

  // A manual refresh re-fetches once the command finishes, even while
  // SignalR refreshes are being held back.
  const manualRefreshState = useRef<'idle' | 'requested' | 'executing'>('idle');
  const [isQueueRowModalOpen, setIsQueueRowModalOpen] = useState(false);

  const [selectState, setSelectState] = useSelectState();
  const { allSelected, allUnselected, selectedState } = selectState;

  const selectedIds = useMemo(() => {
    return getSelectedIds(selectedState);
  }, [selectedState]);

  const isPendingSelected = useMemo(() => {
    return items.some((item) => {
      return selectedIds.indexOf(item.id) > -1 && item.status === 'delay';
    });
  }, [items, selectedIds]);

  const [isConfirmRemoveModalOpen, setIsConfirmRemoveModalOpen] =
    useState(false);

  const isRefreshing =
    isFetching || isLoading || isRefreshMonitoredDownloadsExecuting;
  const isAllPopulated = isFetched;
  const hasError = error;
  const selectedCount = selectedIds.length;
  const disableSelectedActions = selectedCount === 0;

  // Same rule as the row's own manual import button: only completed downloads
  // that could not be imported automatically have files to offer.
  const selectedDownloadIds = useMemo(() => {
    return selectedIds.reduce<string[]>((acc, id) => {
      const item = items.find((i) => i.id === id);

      if (
        item?.downloadId &&
        item.status === 'completed' &&
        item.trackedDownloadStatus === 'warning' &&
        !acc.includes(item.downloadId)
      ) {
        acc.push(item.downloadId);
      }

      return acc;
    }, []);
  }, [items, selectedIds]);

  const [importDownloadIds, setImportDownloadIds] = useState<string[]>([]);

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setSelectState({ type: value ? 'selectAll' : 'unselectAll', items });
    },
    [items, setSelectState]
  );

  const handleSelectedChange = useCallback(
    ({ id, value, shiftKey = false }: SelectStateInputProps) => {
      setSelectState({
        type: 'toggleSelected',
        items,
        id,
        isSelected: value,
        shiftKey,
      });
    },
    [items, setSelectState]
  );

  const handleRefreshPress = useCallback(() => {
    manualRefreshState.current = 'requested';

    executeCommand({
      name: commandNames.REFRESH_MONITORED_DOWNLOADS,
    });
  }, [executeCommand]);

  const handleQueueRowModalOpenOrClose = useCallback((isOpen: boolean) => {
    setIsQueueRowModalOpen(isOpen);
  }, []);

  const handleGrabSelectedPress = useCallback(() => {
    grabQueueItems({ ids: selectedIds });
  }, [selectedIds, grabQueueItems]);

  const handleRemoveSelectedPress = useCallback(() => {
    setIsConfirmRemoveModalOpen(true);
  }, [setIsConfirmRemoveModalOpen]);

  const handleRemoveSelectedConfirmed = useCallback(
    (payload: RemovePressProps) => {
      removeQueueItems({ ids: selectedIds, ...payload });
      setIsConfirmRemoveModalOpen(false);
    },
    [selectedIds, setIsConfirmRemoveModalOpen, removeQueueItems]
  );

  const handleConfirmRemoveModalClose = useCallback(() => {
    setIsConfirmRemoveModalOpen(false);
  }, [setIsConfirmRemoveModalOpen]);

  const handleImportSelectedPress = useCallback(() => {
    setImportDownloadIds(selectedDownloadIds);
  }, [selectedDownloadIds]);

  const handleImportSelectedModalClose = useCallback(() => {
    setImportDownloadIds([]);
  }, []);

  const handleFirstPagePress = useCallback(() => {
    goToPage(1);
  }, [goToPage]);

  const handlePreviousPagePress = useCallback(() => {
    goToPage(Math.max(page - 1, 1));
  }, [goToPage, page]);

  const handleNextPagePress = useCallback(() => {
    goToPage(Math.min(page + 1, totalPages));
  }, [goToPage, page, totalPages]);

  const handleLastPagePress = useCallback(() => {
    goToPage(totalPages);
  }, [goToPage, totalPages]);

  const handlePageSelect = useCallback(
    (pageNumber: number) => {
      goToPage(pageNumber);
    },
    [goToPage]
  );

  const handleFilterSelect = useCallback(
    (selectedFilterKey: string | number) => {
      setQueueOption('selectedFilterKey', selectedFilterKey);
      goToPage(1);
    },
    [goToPage]
  );

  const handleSortPress = useCallback((sortKey: string) => {
    setQueueSort({ sortKey });
  }, []);

  const handleTableOptionChange = useCallback(
    (payload: TableOptionsChangePayload) => {
      setQueueOptions(payload);

      if (payload.pageSize) {
        goToPage(1);
      }
    },
    [goToPage]
  );

  const isRefreshBlocked =
    selectedCount > 0 ||
    isQueueRowModalOpen ||
    isConfirmRemoveModalOpen ||
    importDownloadIds.length > 0 ||
    isRemoving ||
    isGrabbing;

  useEffect(() => {
    setQueueRefreshBlocked(isRefreshBlocked);
  }, [isRefreshBlocked]);

  useEffect(() => {
    if (isRefreshMonitoredDownloadsExecuting) {
      if (manualRefreshState.current === 'requested') {
        manualRefreshState.current = 'executing';
      }

      return;
    }

    if (manualRefreshState.current === 'executing') {
      manualRefreshState.current = 'idle';
      forceRefreshQueue();
    }
  }, [isRefreshMonitoredDownloadsExecuting]);

  useEffect(() => {
    return () => {
      setQueueRefreshBlocked(false);
    };
  }, []);

  return (
    <PageContent title={translate('Queue')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('Refresh')}
            iconName={icons.REFRESH}
            isSpinning={isLoading || isRefreshMonitoredDownloadsExecuting}
            onPress={handleRefreshPress}
          />

          <PageToolbarSeparator />

          <PageToolbarButton
            label={translate('GrabSelected')}
            iconName={icons.DOWNLOAD}
            isDisabled={disableSelectedActions || !isPendingSelected}
            isSpinning={isGrabbing}
            onPress={handleGrabSelectedPress}
          />

          <PageToolbarButton
            label={translate('RemoveSelected')}
            iconName={icons.REMOVE}
            isDisabled={disableSelectedActions}
            isSpinning={isRemoving}
            onPress={handleRemoveSelectedPress}
          />

          <PageToolbarSeparator />

          <PageToolbarButton
            label={translate('ImportSelected')}
            iconName={icons.INTERACTIVE}
            isDisabled={!selectedDownloadIds.length}
            onPress={handleImportSelectedPress}
          />
        </PageToolbarSection>

        <PageToolbarSection alignContent={align.RIGHT}>
          <TableOptionsModalWrapper
            columns={columns}
            pageSize={pageSize}
            maxPageSize={200}
            optionsComponent={QueueOptions}
            onTableOptionChange={handleTableOptionChange}
          >
            <PageToolbarButton
              label={translate('Options')}
              iconName={icons.TABLE}
            />
          </TableOptionsModalWrapper>

          <FilterMenu
            alignMenu={align.RIGHT}
            selectedFilterKey={selectedFilterKey}
            filters={FILTERS as unknown as AppStateFilter[]}
            customFilters={customFilters}
            filterModalConnectorComponent={QueueFilterModal}
            filterModalConnectorComponentProps={{ sectionItems: items }}
            onFilterSelect={handleFilterSelect}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        {isRefreshing && !isAllPopulated ? <LoadingIndicator /> : null}

        {!isRefreshing && hasError && !items.length ? (
          <Alert kind={kinds.DANGER}>{translate('QueueLoadError')}</Alert>
        ) : null}

        {isAllPopulated && !hasError && !items.length ? (
          <Alert kind={kinds.INFO}>
            {selectedFilterKey !== 'all' && count > 0
              ? translate('QueueFilterHasNoItems')
              : translate('QueueIsEmpty')}
          </Alert>
        ) : null}

        {isAllPopulated && !!items.length ? (
          <div>
            <Table
              selectAll={true}
              allSelected={allSelected}
              allUnselected={allUnselected}
              columns={columns}
              pageSize={pageSize}
              sortKey={sortKey}
              sortDirection={sortDirection}
              optionsComponent={QueueOptions}
              onTableOptionChange={handleTableOptionChange}
              onSelectAllChange={handleSelectAllChange}
              onSortPress={handleSortPress}
            >
              <TableBody>
                {items.map((item) => {
                  return (
                    <QueueRow
                      key={item.id}
                      movieId={item.movieId}
                      isSelected={selectedState[item.id]}
                      columns={columns}
                      {...item}
                      onSelectedChange={handleSelectedChange}
                      onQueueRowModalOpenOrClose={
                        handleQueueRowModalOpenOrClose
                      }
                    />
                  );
                })}
              </TableBody>
            </Table>

            <TablePager
              page={page}
              totalPages={totalPages}
              totalRecords={totalRecords}
              isFetching={isFetching && isPlaceholderData}
              onFirstPagePress={handleFirstPagePress}
              onPreviousPagePress={handlePreviousPagePress}
              onNextPagePress={handleNextPagePress}
              onLastPagePress={handleLastPagePress}
              onPageSelect={handlePageSelect}
            />
          </div>
        ) : null}
      </PageContentBody>

      <RemoveQueueItemModal
        isOpen={isConfirmRemoveModalOpen}
        selectedCount={selectedCount}
        canChangeCategory={
          isConfirmRemoveModalOpen &&
          selectedIds.every((id) => {
            const item = items.find((i) => i.id === id);

            return !!(item && item.downloadClientHasPostImportCategory);
          })
        }
        canIgnore={
          isConfirmRemoveModalOpen &&
          selectedIds.every((id) => {
            const item = items.find((i) => i.id === id);

            return !!(item && item.movieId);
          })
        }
        isPending={
          isConfirmRemoveModalOpen &&
          selectedIds.every((id) => {
            const item = items.find((i) => i.id === id);

            if (!item) {
              return false;
            }

            return (
              item.status === 'delay' ||
              item.status === 'downloadClientUnavailable'
            );
          })
        }
        onRemovePress={handleRemoveSelectedConfirmed}
        onModalClose={handleConfirmRemoveModalClose}
      />

      <InteractiveImportModal
        isOpen={importDownloadIds.length > 0}
        downloadIds={importDownloadIds}
        title={translate('InteractiveImportMultipleQueueItems')}
        onModalClose={handleImportSelectedModalClose}
      />
    </PageContent>
  );
}

export default Queue;
