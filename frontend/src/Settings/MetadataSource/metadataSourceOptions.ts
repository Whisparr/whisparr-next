import translate from 'Utilities/String/translate';
import { MetadataSources } from './useMetadataSources';

type SceneSource = MetadataSources['sceneMetadataSource'];
type MovieSource = MetadataSources['movieMetadataSource'];

export interface MetadataSourceOption<T> {
  key: T;
  label: string;
}

// Functions rather than constants: translations load after module evaluation.
export function getSceneSourceOptions(): MetadataSourceOption<SceneSource>[] {
  return [
    { key: 'stashDb', label: translate('StashDB') },
    { key: 'tpdb', label: translate('TPDb') },
  ];
}

// `none` is left out: the API rejects it as a first-run choice.
export function getMovieSourceOptions(): MetadataSourceOption<
  Exclude<MovieSource, 'none'>
>[] {
  return [
    { key: 'tmdb', label: translate('TMDb') },
    { key: 'tpdb', label: translate('TPDb') },
  ];
}

export function getSceneSourceLabel(source: SceneSource) {
  return source === 'tpdb' ? translate('TPDb') : translate('StashDB');
}

export function getMovieSourceLabel(source: MovieSource) {
  switch (source) {
    case 'tmdb':
      return translate('TMDb');
    case 'tpdb':
      return translate('TPDb');
    default:
      return translate('None');
  }
}
