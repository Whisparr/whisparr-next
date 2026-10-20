import { useCallback } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import useQueryClient from 'Helpers/Hooks/useQueryClient';

export interface MetadataSources {
  sceneMetadataSource: 'stashDb' | 'tpdb';
  movieMetadataSource: 'tmdb' | 'tpdb' | 'none';
  locked: boolean;
}

export type MetadataSourcesChoice = Omit<MetadataSources, 'locked'>;

const PATH = '/config/metadatasource';

// The general settings carry the movie source too, and the V5 host-config
// validator rejects a save that changes it, so their cached copy must be
// refreshed once the sources are locked.
const GENERAL_SETTINGS_PATH = '/config/host';

export default function useMetadataSources() {
  const { data, isFetched } = useApiQuery<MetadataSources>({
    path: PATH,
  });

  return { data, isFetched };
}

// Undefined until the sources load, so callers can hold back a link rather
// than point it at the wrong site.
export function useSceneMetadataSource() {
  const { data } = useMetadataSources();

  return data?.sceneMetadataSource;
}

export function useChooseMetadataSources() {
  const queryClient = useQueryClient();

  // Returned to the mutation callbacks, which await it, so `isSaving` holds
  // until the refetched sources have closed the picker.
  const invalidate = useCallback(
    () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: [PATH] }),
        queryClient.invalidateQueries({ queryKey: [GENERAL_SETTINGS_PATH] }),
      ]),
    [queryClient]
  );

  const { mutate, isPending, error } = useApiMutation<
    MetadataSources,
    MetadataSourcesChoice
  >({
    path: PATH,
    method: 'PUT',
    mutationOptions: {
      onSuccess: invalidate,
      onError: (apiError) => {
        // 409: the sources were locked elsewhere (another tab, say). Refetch so
        // the picker closes instead of failing on every retry.
        return apiError.statusCode === 409 ? invalidate() : undefined;
      },
    },
  });

  const choose = useCallback(
    (sources: MetadataSourcesChoice) => {
      mutate({
        sceneMetadataSource: sources.sceneMetadataSource,
        movieMetadataSource: sources.movieMetadataSource,
      });
    },
    [mutate]
  );

  return { choose, isSaving: isPending, error };
}
