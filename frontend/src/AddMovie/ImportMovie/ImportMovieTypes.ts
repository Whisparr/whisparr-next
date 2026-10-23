import { QualityModel } from 'Quality/Quality';
import FingerprintCandidate from 'typings/FingerprintCandidate';

export interface ImportFile {
  name: string;
  path: string;
  relativePath: string;
  candidates?: FingerprintCandidate[] | null;
}

// The scene to preselect for a file: only when its top candidate is an oshash
// hit and no other oshash candidate competes with it.
export function getHashMatchCandidate(
  file: ImportFile
): FingerprintCandidate | null {
  const candidates = file.candidates ?? [];
  const oshash = candidates.filter((c) => c.algorithm === 'OSHASH');

  if (oshash.length !== 1 || candidates[0] !== oshash[0]) {
    return null;
  }

  return oshash[0].sceneId && oshash[0].lookupTerm ? oshash[0] : null;
}

function isSameScene(a?: string | null, b?: string | null): boolean {
  return !!a && !!b && a.toLowerCase() === b.toLowerCase();
}

export function isHashMatch(item: ImportItem): boolean {
  const { hashSceneId, selectedMovie } = item;

  // An entry already in the library carries its own chip, so it needs no label.
  return (
    !!selectedMovie &&
    !selectedMovie.isExisting &&
    isSameScene(selectedMovie.foreignId, hashSceneId)
  );
}

// The movies/scenes routes pin the lookup to one type; the bare
// /add/import/:rootFolderId route leaves it unset so the server infers the
// type per file.
export type ImportItemType = 'movie' | 'scene';

// Only the fields the import table reads. Credits carry the performer gender,
// but the server skips mapping them for results that already exist locally —
// those fall back to performerNames.
export interface ImportCredit {
  personName?: string;
  performer?: {
    name?: string;
    gender?: string;
  };
}

export interface MovieLookupResult {
  foreignId: string;
  tmdbId: number;
  tpdbId?: string;
  itemType: ImportItemType;
  title: string;
  year: number;
  releaseDate?: string;
  studioTitle?: string;
  performerNames?: string[];
  searchCredits?: ImportCredit[];
  isExisting: boolean;
  // Only populated for results already in the library, so the import table can say
  // whether the entry is still missing its file or which quality it already holds.
  hasFile?: boolean;
  sizeOnDisk?: number;
  existingQuality?: QualityModel;
}

export interface ImportItem {
  id: string;
  path: string;
  relativePath: string;
  term: string;
  itemType?: ImportItemType;
  isFetching: boolean;
  isPopulated: boolean;
  isQueued: boolean;
  error: Error | null;
  items: MovieLookupResult[];
  selectedMovie: MovieLookupResult | null;
  // The scene suggested by the file's fingerprint; the label shows only while
  // that scene is the selected one.
  hashSceneId: string | null;
  monitor: string;
  qualityProfileId: number;
}

export type ImportAction =
  | {
      type: 'INIT_ITEMS';
      files: ReadonlyArray<ImportFile>;
      itemType?: ImportItemType;
      defaults: { monitor: string; qualityProfileId: number };
    }
  | { type: 'SET_ITEM_FETCHING'; id: string }
  | {
      type: 'SET_LOOKUP_RESULTS';
      id: string;
      results: MovieLookupResult[];
    }
  | { type: 'SET_LOOKUP_ERROR'; id: string; error: Error }
  | { type: 'SET_SELECTED_MOVIE'; id: string; movie: MovieLookupResult | null }
  | {
      type: 'SET_ITEM_VALUE';
      id: string;
      key: 'monitor' | 'qualityProfileId';
      value: string | number;
    }
  | { type: 'SET_QUEUED'; id: string; queued: boolean }
  | { type: 'REMOVE_ITEM'; id: string }
  | { type: 'SET_LOOKING_UP'; value: boolean };

export interface ImportState {
  isLookingUp: boolean;
  items: ImportItem[];
}

export const IMPORT_ITEM_LIMIT = 500;

export function importReducer(
  state: ImportState,
  action: ImportAction
): ImportState {
  switch (action.type) {
    case 'INIT_ITEMS': {
      const sliced = action.files.slice(0, IMPORT_ITEM_LIMIT);
      return {
        isLookingUp: false,
        items: sliced.map((f) => {
          const hashCandidate = getHashMatchCandidate(f);

          return {
            id: f.name,
            path: f.path,
            relativePath: f.relativePath,
            term: hashCandidate?.lookupTerm ?? f.name,
            itemType: hashCandidate ? ('scene' as const) : action.itemType,
            isFetching: false,
            isPopulated: false,
            isQueued: false,
            error: null,
            items: [],
            selectedMovie: null,
            hashSceneId: hashCandidate?.sceneId ?? null,
            monitor: action.defaults.monitor,
            qualityProfileId: action.defaults.qualityProfileId,
          };
        }),
      };
    }

    case 'SET_ITEM_FETCHING':
      return {
        ...state,
        items: state.items.map((item) =>
          item.id === action.id ? { ...item, isFetching: true } : item
        ),
      };

    case 'SET_LOOKUP_RESULTS':
      return {
        ...state,
        items: state.items.map((item) => {
          if (item.id !== action.id) {
            return item;
          }

          // The suggested scene wins over the server's ranking when it is in the results
          const hashResult = action.results.find((r) =>
            isSameScene(r.foreignId, item.hashSceneId)
          );
          const selectedMovie =
            item.selectedMovie ?? hashResult ?? action.results[0] ?? null;
          return {
            ...item,
            isFetching: false,
            isPopulated: true,
            isQueued: false,
            error: null,
            items: action.results,
            selectedMovie,
            // Without the suggested scene in the results the hash match is dropped
            hashSceneId:
              hashResult ||
              isSameScene(selectedMovie?.foreignId, item.hashSceneId)
                ? item.hashSceneId
                : null,
          };
        }),
      };

    case 'SET_LOOKUP_ERROR':
      return {
        ...state,
        items: state.items.map((item) =>
          item.id === action.id
            ? {
                ...item,
                isFetching: false,
                isPopulated: false,
                isQueued: false,
                error: action.error,
              }
            : item
        ),
      };

    case 'SET_SELECTED_MOVIE':
      return {
        ...state,
        items: state.items.map((item) =>
          item.id === action.id
            ? { ...item, selectedMovie: action.movie }
            : item
        ),
      };

    case 'SET_ITEM_VALUE':
      return {
        ...state,
        items: state.items.map((item) =>
          item.id === action.id ? { ...item, [action.key]: action.value } : item
        ),
      };

    case 'SET_QUEUED':
      return {
        ...state,
        items: state.items.map((item) =>
          item.id === action.id ? { ...item, isQueued: action.queued } : item
        ),
      };

    case 'REMOVE_ITEM':
      return {
        ...state,
        items: state.items.filter((item) => item.id !== action.id),
      };

    case 'SET_LOOKING_UP':
      return { ...state, isLookingUp: action.value };

    default:
      return state;
  }
}
