import { createContext, useContext } from 'react';
import Column from 'Components/Table/Column';
import {
  createOptionsStore,
  OptionChanged,
} from 'Helpers/Hooks/useOptionsStore';
import { resetPage } from 'Helpers/Hooks/usePage';
import { sortDirections } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import translate from 'Utilities/String/translate';

export interface MovieIndexPosterOptions {
  detailedProgressBar: boolean;
  size: string;
  showTitle: boolean;
  showMonitored: boolean;
  showQualityProfile: boolean;
  showReleaseDate: boolean;
  showTmdbRating: boolean;
  showTags: boolean;
  showSearchAction: boolean;
  pageSize: number;
}

export interface MovieIndexOverviewOptions {
  detailedProgressBar: boolean;
  size: string;
  showMonitored: boolean;
  showStudio: boolean;
  showQualityProfile: boolean;
  showAdded: boolean;
  showPath: boolean;
  showSizeOnDisk: boolean;
  showTags: boolean;
  showSearchAction: boolean;
  pageSize: number;
}

export interface MovieIndexTableOptions {
  showSearchAction: boolean;
  pageSize: number;
}

export interface MovieIndexOptions {
  indexMode: string;
  view: string;
  sortKey: string;
  sortDirection: SortDirection;
  selectedFilterKey: string | number;
  columns: Column[];
  posterOptions: MovieIndexPosterOptions;
  overviewOptions: MovieIndexOverviewOptions;
  tableOptions: MovieIndexTableOptions;
}

export type MovieIndexItemType = 'movie' | 'jav';

// One store per index, so /movies and /jav keep their own view, sort, columns,
// filter and page. The movie key is unchanged so existing settings survive.
export function createMovieIndexStore(itemType: MovieIndexItemType) {
  const pageKey: 'javIndex' | 'movieIndex' =
    itemType === 'jav' ? 'javIndex' : 'movieIndex';

  const { useOptions, useOption, getOptions, setOptions, setOption, setSort } =
    createOptionsStore<MovieIndexOptions>(
      itemType === 'jav' ? 'jav_index_options' : 'movie_index_options',
      () => {
        return {
          indexMode: 'movie',
          view: 'posters',
          sortKey: 'cleanTitle',
          sortDirection: sortDirections.ASCENDING,
          selectedFilterKey: 'all',

          posterOptions: {
            detailedProgressBar: false,
            size: 'large',
            showTitle: false,
            showMonitored: true,
            showQualityProfile: true,
            showReleaseDate: false,
            showTmdbRating: false,
            showTags: false,
            showSearchAction: false,
            pageSize: 25,
          },

          overviewOptions: {
            detailedProgressBar: false,
            size: 'medium',
            showMonitored: true,
            showStudio: true,
            showQualityProfile: true,
            showAdded: false,
            showPath: false,
            showSizeOnDisk: false,
            showTags: false,
            showSearchAction: false,
            pageSize: 20,
          },

          tableOptions: {
            showSearchAction: false,
            pageSize: 20,
          },

          columns: [
            {
              name: 'select',
              label: '',
              columnLabel: 'Select',
              isSortable: false,
              isVisible: true,
              isModifiable: 'disabled',
            },
            {
              name: 'status',
              label: '',
              columnLabel: () => translate('ReleaseStatus'),
              isSortable: false,
              isVisible: true,
              isModifiable: 'disabled',
            },
            {
              name: 'sortTitle',
              label: () => translate('MovieTitle'),
              isSortable: true,
              isVisible: true,
              isModifiable: 'disabled',
            },
            {
              name: 'studioTitle',
              label: () => translate('Studio'),
              isSortable: true,
              isVisible: true,
            },
            {
              name: 'qualityProfileId',
              label: () => translate('QualityProfile'),
              isSortable: true,
              isVisible: true,
            },
            {
              name: 'added',
              label: () => translate('Added'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'releaseDate',
              label: () => translate('ReleaseDate'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'year',
              label: () => translate('Year'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'runtime',
              label: () => translate('Runtime'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'path',
              label: () => translate('Path'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'sizeOnDisk',
              label: () => translate('SizeOnDisk'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'genres',
              label: () => translate('Genres'),
              isSortable: false,
              isVisible: false,
            },
            {
              name: 'movieStatus',
              label: () => translate('Status'),
              isSortable: true,
              isVisible: true,
            },
            {
              name: 'releaseGroups',
              label: () => translate('ReleaseGroup'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'tags',
              label: () => translate('Tags'),
              isSortable: true,
              isVisible: false,
            },
            {
              name: 'actions',
              label: '',
              columnLabel: () => translate('Actions'),
              isVisible: true,
              isModifiable: 'disabled',
            },
          ],
        };
      }
    );

  // Sorting, filtering and switching view all invalidate the current page
  // number -- page 3 of a poster view is not page 3 of a differently sorted
  // or filtered list.
  const setMovieIndexSort = (sortKey: string) => {
    setSort({ sortKey });
    resetPage(pageKey);
  };

  const setMovieIndexFilter = (selectedFilterKey: string | number) => {
    setOption('selectedFilterKey', selectedFilterKey);
    resetPage(pageKey);
  };

  const setMovieIndexView = (view: string) => {
    setOption('view', view);
    resetPage(pageKey);
  };

  // The poster and overview option modals send a partial set of their own
  // keys, so those merge into the sub-object rather than replacing it.
  const setMovieIndexPosterOption = (
    payload: Partial<MovieIndexPosterOptions>
  ) => {
    setOption('posterOptions', { ...getOptions().posterOptions, ...payload });
  };

  const setMovieIndexOverviewOption = (
    payload: Partial<MovieIndexOverviewOptions>
  ) => {
    setOption('overviewOptions', {
      ...getOptions().overviewOptions,
      ...payload,
    });
  };

  // The table options modal sends `{ tableOptions }`; the column modal sends
  // `{ columns }`.
  const setMovieIndexTableOption = (
    payload: Partial<Pick<MovieIndexOptions, 'columns' | 'tableOptions'>>
  ) => {
    setOptions(payload);
  };

  return {
    itemType,
    pageKey,
    // The custom-filter type is a free string server-side.
    customFilterType: pageKey,
    useMovieIndexOptions: useOptions,
    useMovieIndexOption: useOption,
    setMovieIndexOption: setOption,
    setMovieIndexSort,
    setMovieIndexFilter,
    setMovieIndexView,
    setMovieIndexPosterOption,
    setMovieIndexOverviewOption,
    setMovieIndexTableOption,
  };
}

export type MovieIndexStore = ReturnType<typeof createMovieIndexStore>;

export const movieIndexOptionsStore = createMovieIndexStore('movie');
export const javIndexOptionsStore = createMovieIndexStore('jav');

export const getMovieIndexStore = (itemType: MovieIndexItemType) =>
  itemType === 'jav' ? javIndexOptionsStore : movieIndexOptionsStore;

export const MovieIndexStoreContext = createContext<MovieIndexStore>(
  movieIndexOptionsStore
);

export const useMovieIndexStore = () => useContext(MovieIndexStoreContext);

// Outside the index (search results) the movie store applies.
export const useMovieIndexOption = movieIndexOptionsStore.useMovieIndexOption;

export type MovieIndexOptionChanged = OptionChanged<MovieIndexOptions>;
