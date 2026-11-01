import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { useUnmappedFilesOptions } from './unmappedFilesOptionsStore';

export const UNMAPPED_FILES_PATH = '/unmappedfile';

const PATH = UNMAPPED_FILES_PATH;

export type UnmappedItemType = 'scene' | 'movie';

// Present only when the stored top suggestion matches on both studio and date
export interface UnmappedFileMatch {
  foreignId: string;
  title: string;
  studioTitle?: string;
  releaseDate?: string;
  hashMatch: boolean;
}

export interface UnmappedFile {
  id: number;
  rootFolderId: number;
  path: string;
  name: string;
  exists: boolean;
  dismissed: boolean;
  match?: UnmappedFileMatch;
}

export interface UnmappedFileSuggestion {
  foreignId: string;
  title: string;
  studioTitle?: string;
  releaseDate?: string;
  itemType: UnmappedItemType;
  isExisting: boolean;
  hasFile: boolean;
  hashMatch: boolean;
  sameDate: boolean;
  sameStudio: boolean;
  titleSimilarity: number;
}

interface UnmappedFileSuggestions {
  term: string;
  results: UnmappedFileSuggestion[];
}

interface LinkData {
  id: number;
  foreignId: string;
  itemType: UnmappedItemType;
  qualityProfileId: number;
  monitored: boolean;
}

interface DismissData {
  ids: number[];
  dismissed: boolean;
}

const useUnmappedFiles = (term: string, dismissed: boolean) => {
  const { page, goToPage } = usePage('unmappedFiles');
  const { pageSize, sortKey, sortDirection } = useUnmappedFilesOptions();

  const query = usePagedApiQuery<UnmappedFile>({
    path: PATH,
    page,
    pageSize,
    sortKey,
    sortDirection,
    queryParams: { term, dismissed },
  });

  return { ...query, page, goToPage };
};

export const useUnmappedFileSuggestions = (
  id: number,
  term: string,
  itemType: UnmappedItemType,
  enabled: boolean
) => {
  return useApiQuery<UnmappedFileSuggestions>({
    path: `${PATH}/${id}/suggestions`,
    queryParams: { term, itemType },
    queryOptions: { enabled },
  });
};

export const useLinkUnmappedFile = () => {
  const queryClient = useQueryClient();

  return useApiMutation<unknown, LinkData, Omit<LinkData, 'id'>>({
    method: 'POST',
    path: ({ id }) => `${PATH}/${id}/link`,
    body: ({ id: _id, ...body }) => body,
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
        queryClient.invalidateQueries({ queryKey: ['/movie/paged'] });
        queryClient.invalidateQueries({ queryKey: ['/movie/stats'] });
      },
    },
  });
};

export const useDismissUnmappedFiles = () => {
  const queryClient = useQueryClient();

  return useApiMutation<unknown, DismissData>({
    method: 'PUT',
    path: `${PATH}/dismiss`,
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
      },
    },
  });
};

export default useUnmappedFiles;
