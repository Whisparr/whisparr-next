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
