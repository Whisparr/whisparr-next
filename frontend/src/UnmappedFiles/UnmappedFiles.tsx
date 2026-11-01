import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { SelectProvider } from 'App/SelectContext';
import * as commandNames from 'Commands/commandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import useDebounce from 'Helpers/Hooks/useDebounce';
import usePrevious from 'Helpers/Hooks/usePrevious';
import useSelectState from 'Helpers/Hooks/useSelectState';
import { align, icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import { CheckInputChanged, InputChanged } from 'typings/inputs';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import UnmappedFileRow from './UnmappedFileRow';
import {
  setUnmappedFilesSort,
  useUnmappedFilesOptions,
} from './unmappedFilesOptionsStore';
import useUnmappedFiles, { useDismissUnmappedFiles } from './useUnmappedFiles';

function UnmappedFiles() {
  const [term, setTerm] = useState('');
  const [showDismissed, setShowDismissed] = useState(false);
  const debouncedTerm = useDebounce(term, 300);

  const { columns, sortKey, sortDirection } = useUnmappedFilesOptions();

  const {
    records: items,
    totalPages,
    totalRecords,
    isFetching,
    isFetched,
    isLoading,
    error,
    page,
    goToPage,
  } = useUnmappedFiles(debouncedTerm, showDismissed);

  const queryClient = useQueryClient();
  const executeCommand = useExecuteCommand();
  const isScanning = useCommandExecuting(commandNames.SCAN_UNMAPPED_FILES);
  const wasScanning = usePrevious(isScanning);
  const isFindingMatches = useCommandExecuting(
    commandNames.FIND_UNMAPPED_FILE_MATCHES
  );
  const { mutate: dismiss, isPending: isDismissing } =
    useDismissUnmappedFiles();

  const [selectState, setSelectState] = useSelectState();
  const { allSelected, allUnselected, selectedState } = selectState;

  const selectedIds = useMemo(() => {
    return getSelectedIds(selectedState);
  }, [selectedState]);

  useEffect(() => {
    if (wasScanning && !isScanning) {
      queryClient.invalidateQueries({ queryKey: ['/unmappedfile'] });
    }
  }, [isScanning, wasScanning, queryClient]);

  const handleTermChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setTerm(value);
      goToPage(1);
    },
    [goToPage]
  );

  const handleScanPress = useCallback(() => {
    executeCommand({ name: commandNames.SCAN_UNMAPPED_FILES });
  }, [executeCommand]);

  // The rows on the current page that can be searched; the job never runs on
  // anything else, and nothing else starts it
  const matchableIds = useMemo(
    () => items.filter((f) => f.exists && !f.dismissed).map((f) => f.id),
    [items]
  );

  const handleFindMatchesPress = useCallback(() => {
    executeCommand({
      name: commandNames.FIND_UNMAPPED_FILE_MATCHES,
      fileIds: matchableIds,
    });
  }, [executeCommand, matchableIds]);

  const handleShowDismissedPress = useCallback(() => {
    setShowDismissed((value) => !value);
    setSelectState({ type: 'reset' });
    goToPage(1);
  }, [goToPage, setSelectState]);

  const handleDismissSelectedPress = useCallback(() => {
    dismiss({ ids: selectedIds, dismissed: !showDismissed });
    setSelectState({ type: 'unselectAll', items });
  }, [dismiss, items, selectedIds, setSelectState, showDismissed]);

  const handleDismissPress = useCallback(
    (id: number, dismissed: boolean) => {
      dismiss({ ids: [id], dismissed });
    },
    [dismiss]
  );

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

  const handleSortPress = useCallback(
    (sortKey: string, sortDirection?: SortDirection) => {
      setUnmappedFilesSort({ sortKey, sortDirection });
    },
    []
  );

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

  return (
    <SelectProvider items={items}>
      <PageContent title={translate('UnmappedFiles')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('ScanRootFolders')}
              iconName={icons.REFRESH}
              isSpinning={isScanning}
              onPress={handleScanPress}
            />

            <PageToolbarButton
              label={translate('FindMatches')}
              iconName={icons.SEARCH}
              isSpinning={isFindingMatches}
              isDisabled={isFindingMatches || !matchableIds.length}
              onPress={handleFindMatchesPress}
            />

            <PageToolbarButton
              label={translate(showDismissed ? 'Restore' : 'Dismiss')}
              iconName={showDismissed ? icons.RESTORE : icons.HIDDEN}
              isDisabled={!selectedIds.length}
              isSpinning={isDismissing}
              onPress={handleDismissSelectedPress}
            />
          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <PageToolbarButton
              label={translate(
                showDismissed ? 'ShowUnmappedFiles' : 'ShowDismissed'
              )}
              iconName={showDismissed ? icons.VIEW : icons.HIDDEN}
              onPress={handleShowDismissedPress}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody>
          <TextInput
            name="term"
            value={term}
            placeholder={translate('UnmappedFilesFilterPlaceholder')}
            onChange={handleTermChange}
          />

          {isLoading && !isFetched ? <LoadingIndicator /> : null}

          {!isLoading && !!error ? (
            <Alert kind={kinds.DANGER}>
              {translate('UnmappedFilesLoadError')}
            </Alert>
          ) : null}

          {isFetched && !error && !items.length ? (
            <Alert kind={kinds.INFO}>{translate('UnmappedFilesEmpty')}</Alert>
          ) : null}

          {isFetched && !error && !!items.length ? (
            <div>
              <Table
                selectAll={true}
                allSelected={allSelected}
                allUnselected={allUnselected}
                columns={columns}
                sortKey={sortKey}
                sortDirection={sortDirection}
                onSelectAllChange={handleSelectAllChange}
                onSortPress={handleSortPress}
              >
                <TableBody>
                  {items.map((item) => (
                    <UnmappedFileRow
                      key={item.id}
                      file={item}
                      isSelected={selectedState[item.id] || false}
                      onSelectedChange={handleSelectedChange}
                      onDismissPress={handleDismissPress}
                    />
                  ))}
                </TableBody>
              </Table>

              <TablePager
                page={page}
                totalPages={totalPages}
                totalRecords={totalRecords}
                isFetching={isFetching}
                onFirstPagePress={handleFirstPagePress}
                onPreviousPagePress={handlePreviousPagePress}
                onNextPagePress={handleNextPagePress}
                onLastPagePress={handleLastPagePress}
                onPageSelect={goToPage}
              />
            </div>
          ) : null}
        </PageContentBody>
      </PageContent>
    </SelectProvider>
  );
}

export default UnmappedFiles;
