import { useMovieMonitorAvailability } from './useMovieMonitorAvailability';

export function useShowMovieMonitorToggleButton(
  tmdbid?: number,
  tpdbid?: string
) {
  // The entity kind only picks the warning text, which isn't used here.
  const { isSupported, isLinked } = useMovieMonitorAvailability(
    'studio',
    tmdbid,
    tpdbid
  );

  return isSupported && isLinked;
}
