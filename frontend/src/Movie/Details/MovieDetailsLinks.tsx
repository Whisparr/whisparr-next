import React, { useMemo } from 'react';
import Label from 'Components/Label';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import { MetadataSources } from 'Settings/MetadataSource/useMetadataSources';
import {
  sceneSourceLink,
  translateSourceLabel,
} from 'Utilities/MetadataSource/sourceLinks';
import translate from 'Utilities/String/translate';
import styles from './MovieDetailsLinks.module.css';

interface MovieDetailsLinksProps {
  tmdbId?: number;
  tpdbId?: string;
  stashId?: string;
  // A TPDB scene carries its own key in `tpdbId` (the scene UUID), not a movie id —
  // only link it as /movies/ when this is actually a movie.
  foreignId?: string;
  itemType?: string;
  // Set when the scene's foreign id comes from the locked scene source rather than
  // always from StashDB.
  sceneSource?: MetadataSources['sceneMetadataSource'];
  website?: string;
}

interface MovieDetailsLink {
  name: string;
  url: string;
  // The id the link is built from, when there is one worth copying. The
  // homepage is a bare URL, so it has none.
  externalId?: string;
}

function MovieDetailsLinks({
  tmdbId,
  tpdbId,
  stashId,
  foreignId,
  itemType,
  sceneSource = 'stashDb',
  website,
}: Readonly<MovieDetailsLinksProps>) {
  const links = useMemo(() => {
    const validLinks: MovieDetailsLink[] = [];

    if (website) {
      validLinks.push({
        name: translate('Homepage'),
        url: website,
      });
    }

    if (tmdbId) {
      validLinks.push({
        name: translate('TMDb'),
        url: `https://www.themoviedb.org/movie/${tmdbId}`,
        externalId: `${tmdbId}`,
      });
    }

    // A TPDB scene's `tpdbId` is its own key, not a movie id — never link it as /movies/.
    if (tpdbId && itemType !== 'scene') {
      validLinks.push({
        name: translate('TPDb'),
        url: `https://theporndb.net/movies/${tpdbId}`,
        externalId: tpdbId,
      });
    }

    // Scenes are linked from the locked scene source's foreign id — for TPDB scenes that is
    // `foreignId` (their UUID), while StashDB scenes carry it in `stashId`.
    const sceneForeignId =
      itemType === 'scene' ? (stashId ?? foreignId) : stashId;

    if (sceneForeignId && sceneForeignId !== tmdbId?.toString()) {
      const { url, label } = sceneSourceLink(
        'scene',
        sceneForeignId,
        sceneSource
      );

      validLinks.push({
        name: translateSourceLabel(label),
        url,
        externalId: sceneForeignId,
      });
    }

    return validLinks;
  }, [tmdbId, tpdbId, stashId, foreignId, itemType, sceneSource, website]);

  return (
    <div className={styles.links}>
      {links.map(({ name, url, externalId }) => (
        <div key={name} className={styles.linkBlock}>
          <Link className={styles.link} to={url}>
            <Label
              className={externalId ? styles.linkLabel : styles.soleLinkLabel}
              kind={kinds.INFO}
              size={sizes.LARGE}
            >
              {name}
            </Label>
          </Link>

          {externalId ? (
            <ClipboardButton
              value={externalId}
              title={translate('CopyToClipboard')}
              kind={kinds.DEFAULT}
              size={sizes.SMALL}
            />
          ) : null}
        </div>
      ))}
    </div>
  );
}

export default MovieDetailsLinks;
