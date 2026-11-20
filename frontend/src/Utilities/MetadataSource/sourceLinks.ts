import { MetadataSources } from 'Settings/MetadataSource/useMetadataSources';
import translate from 'Utilities/String/translate';

export type SceneSourceKind = 'scene' | 'studio' | 'performer';

export interface SceneSourceLink {
  url: string;
  label: 'StashDB' | 'TPDB';
}

const stashDbPaths: Record<SceneSourceKind, string> = {
  scene: 'scenes',
  studio: 'studios',
  performer: 'performers',
};

// TPDB calls studios "sites".
const tpdbPaths: Record<SceneSourceKind, string> = {
  scene: 'scenes',
  studio: 'sites',
  performer: 'performers',
};

// Scene, studio and performer foreign ids are UUIDs from the locked scene
// source, so the link follows that source.
export function sceneSourceLink(
  kind: SceneSourceKind,
  foreignId: string,
  source: MetadataSources['sceneMetadataSource']
): SceneSourceLink {
  if (source === 'tpdb') {
    return {
      url: `https://theporndb.net/${tpdbPaths[kind]}/${foreignId}`,
      label: 'TPDB',
    };
  }

  return {
    url: `https://stashdb.org/${stashDbPaths[kind]}/${foreignId}`,
    label: 'StashDB',
  };
}

// The display name for a link label. The TPDB string is keyed `TPDb`, and the
// server's localization lookup ignores case, so a separate `TPDB` key can't be
// added.
export function translateSourceLabel(label: SceneSourceLink['label']) {
  return translate(label === 'TPDB' ? 'TPDb' : 'StashDB');
}

const TPDB_ID_PREFIX = 'tpdbid:';

// The page for a search result's own foreign id, or undefined when it can't
// be built. A scene's id is from the locked scene source, a JAV id is always
// TPDB's, and a movie's is a TMDb id or a `tpdbid:` TPDB movie id.
export function itemSourceLink(
  itemType: string,
  foreignId: string,
  sceneSource: MetadataSources['sceneMetadataSource'] | undefined
): { url: string; name: string } | undefined {
  if (!foreignId) {
    return undefined;
  }

  if (itemType === 'scene') {
    if (!sceneSource) {
      return undefined;
    }

    const { url, label } = sceneSourceLink('scene', foreignId, sceneSource);

    return { url, name: translateSourceLabel(label) };
  }

  if (itemType === 'jav') {
    return {
      url: `https://theporndb.net/jav/${foreignId}`,
      name: translate('TPDb'),
    };
  }

  if (foreignId.startsWith(TPDB_ID_PREFIX)) {
    return {
      url: `https://theporndb.net/movies/${foreignId.slice(TPDB_ID_PREFIX.length)}`,
      name: translate('TPDb'),
    };
  }

  return /^\d+$/.test(foreignId)
    ? {
        url: `https://www.themoviedb.org/movie/${foreignId}`,
        name: translate('TMDb'),
      }
    : undefined;
}
