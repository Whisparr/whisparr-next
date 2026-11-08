import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface MovieStats {
  totalCount: number;
  monitoredCount: number;
  movieFiles: number;
  totalFileSize: number;
}

export function useMovieStats(itemType: 'movie' | 'jav' = 'movie') {
  return useApiQuery<MovieStats>({
    path: '/movie/stats',
    queryParams: { itemType },
  });
}
