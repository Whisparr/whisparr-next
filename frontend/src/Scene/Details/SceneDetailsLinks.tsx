import React from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import { useSceneMetadataSource } from 'Settings/MetadataSource/useMetadataSources';
import {
  sceneSourceLink,
  translateSourceLabel,
} from 'Utilities/MetadataSource/sourceLinks';
import styles from './SceneDetailsLinks.module.css';

export interface SceneDetailsLinksProps {
  foreignId: string;
}

function SceneDetailsLinks({ foreignId }: Readonly<SceneDetailsLinksProps>) {
  const sceneSource = useSceneMetadataSource();

  const sourceLink = sceneSource
    ? sceneSourceLink('scene', foreignId, sceneSource)
    : null;

  return (
    <div className={styles.links}>
      {sourceLink ? (
        <Link className={styles.link} to={sourceLink.url}>
          <Label
            className={styles.linkLabel}
            kind={kinds.INFO}
            size={sizes.LARGE}
          >
            {translateSourceLabel(sourceLink.label)}
          </Label>
        </Link>
      ) : null}
    </div>
  );
}

export default SceneDetailsLinks;
