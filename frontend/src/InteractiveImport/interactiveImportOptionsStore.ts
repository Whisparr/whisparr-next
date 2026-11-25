import { createOptionsStore } from 'Helpers/Hooks/useOptionsStore';
import { SortDirection } from 'Helpers/Props/sortDirections';
import ImportMode from './ImportMode';

export interface InteractiveImportOptions {
  sortKey: string;
  sortDirection: SortDirection;
  importMode: ImportMode;
}

// Replaces the `interactiveImport.sortKey` / `.sortDirection` / `.importMode`
// entries of `persistState`. The table's columns are not here: the slice never
// persisted them, and their visibility is computed per open from `showMovie`
// and whether any item carries indexer flags.
function createInteractiveImportOptionsStore(name: string) {
  return createOptionsStore<InteractiveImportOptions>(name, () => ({
    sortKey: 'relativePath',
    sortDirection: 'ascending',
    importMode: 'chooseImportMode',
  }));
}

export type InteractiveImportOptionsStore = ReturnType<
  typeof createInteractiveImportOptionsStore
>;

export const interactiveImportOptionsStore =
  createInteractiveImportOptionsStore('interactive_import_options');

// Manage Files on a movie or scene keeps its own sort, so opening it no longer
// resets the sort Manual Import remembers.
export const manageFilesOptionsStore = createInteractiveImportOptionsStore(
  'manage_files_options'
);
