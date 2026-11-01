import {
  createOptionsStore,
  PageableOptions,
} from 'Helpers/Hooks/useOptionsStore';
import translate from 'Utilities/String/translate';

const { useOptions, setOptions, setSort } = createOptionsStore<PageableOptions>(
  'unmapped_files_options',
  () => {
    return {
      pageSize: 50,
      selectedFilterKey: 'all',
      sortKey: 'path',
      sortDirection: 'ascending',
      columns: [
        {
          name: 'path',
          label: () => translate('Path'),
          isSortable: true,
          isVisible: true,
        },
        {
          name: 'actions',
          label: '',
          columnLabel: () => translate('Actions'),
          isSortable: false,
          isVisible: true,
        },
      ],
    };
  }
);

export const useUnmappedFilesOptions = useOptions;
export const setUnmappedFilesOptions = setOptions;
export const setUnmappedFilesSort = setSort;
