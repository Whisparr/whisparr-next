import React from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import Performer from 'Performer/Performer';
import { useSceneMetadataSource } from 'Settings/MetadataSource/useMetadataSources';
import {
  sceneSourceLink,
  translateSourceLabel,
} from 'Utilities/MetadataSource/sourceLinks';
import translate from 'Utilities/String/translate';
import styles from './PerformerDetailsLinks.module.css';

type PerformerDetailsLinksProps = Pick<
  Performer,
  'tmdbId' | 'tpdbId' | 'foreignId'
>;

function PerformerDetailsLinks(props: Readonly<PerformerDetailsLinksProps>) {
  const { tmdbId, tpdbId, foreignId } = props;
  const sceneSource = useSceneMetadataSource();

  const sourceLink =
    foreignId && sceneSource
      ? sceneSourceLink('performer', foreignId, sceneSource)
      : null;

  // Under TPDB the foreign id is the TPDB id, so the source link already
  // covers it.
  const showTpdbLink =
    !!tpdbId && !(sceneSource === 'tpdb' && tpdbId === foreignId);

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

      {tmdbId ? (
        <Link
          className={styles.link}
          to={`https://www.themoviedb.org/person/${tmdbId}`}
        >
          <Label
            className={styles.linkLabel}
            kind={kinds.INFO}
            size={sizes.LARGE}
          >
            {translate('TMDb')}
          </Label>
        </Link>
      ) : null}

      {showTpdbLink ? (
        <Link
          className={styles.link}
          to={`https://theporndb.net/performers/${tpdbId}`}
        >
          <Label
            className={styles.linkLabel}
            kind={kinds.INFO}
            size={sizes.LARGE}
          >
            {translate('TPDB')}
          </Label>
        </Link>
      ) : null}
    </div>
  );
}

export default PerformerDetailsLinks;
