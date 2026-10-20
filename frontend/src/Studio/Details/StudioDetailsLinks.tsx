import React from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import { useSceneMetadataSource } from 'Settings/MetadataSource/useMetadataSources';
import {
  sceneSourceLink,
  translateSourceLabel,
} from 'Utilities/MetadataSource/sourceLinks';
import translate from 'Utilities/String/translate';
import styles from './StudioDetailsLinks.module.css';

interface StudioDetailsLinksProps {
  foreignId: string;
  tmdbId?: number;
  tpdbId?: string;
  website?: string;
}

function StudioDetailsLinks({
  foreignId,
  tmdbId,
  tpdbId,
  website,
}: Readonly<StudioDetailsLinksProps>) {
  const sceneSource = useSceneMetadataSource();

  const sourceLink = sceneSource
    ? sceneSourceLink('studio', foreignId, sceneSource)
    : null;

  // Under TPDB the foreign id is the TPDB id, so the source link already
  // covers it.
  const showTpdbLink =
    !!tpdbId && !(sceneSource === 'tpdb' && tpdbId === foreignId);

  return (
    <div className={styles.links}>
      {website && (
        <Link className={styles.link} to={website}>
          <Label
            className={styles.linkLabel}
            kind={kinds.INFO}
            size={sizes.LARGE}
          >
            {translate('Homepage')}
          </Label>
        </Link>
      )}
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

      {!!tmdbId && (
        <Link
          className={styles.link}
          to={`https://www.themoviedb.org/company/${tmdbId}`}
        >
          <Label
            className={styles.linkLabel}
            kind={kinds.INFO}
            size={sizes.LARGE}
          >
            {translate('TMDb')}
          </Label>
        </Link>
      )}

      {showTpdbLink && (
        <Link
          className={styles.link}
          to={`https://theporndb.net/sites/${tpdbId}`}
        >
          <Label
            className={styles.linkLabel}
            kind={kinds.INFO}
            size={sizes.LARGE}
          >
            {translate('TPDb')}
          </Label>
        </Link>
      )}
    </div>
  );
}

export default StudioDetailsLinks;
