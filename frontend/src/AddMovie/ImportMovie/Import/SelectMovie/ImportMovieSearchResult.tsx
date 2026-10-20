import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import { useSceneMetadataSource } from 'Settings/MetadataSource/useMetadataSources';
import {
  sceneSourceLink,
  translateSourceLabel,
} from 'Utilities/MetadataSource/sourceLinks';
import { MovieLookupResult } from '../../ImportMovieTypes';
import ImportMovieTitle from './ImportMovieTitle';
import styles from './ImportMovieSearchResult.module.css';

interface ImportMovieSearchResultProps {
  item: MovieLookupResult;
  onPress: (foreignId: string) => void;
}

function ImportMovieSearchResult({
  item,
  onPress,
}: Readonly<ImportMovieSearchResultProps>) {
  const {
    foreignId,
    tmdbId,
    tpdbId,
    itemType,
    title,
    year,
    releaseDate,
    studioTitle,
    performerNames,
    searchCredits,
    isExisting,
    hasFile,
    existingQuality,
    sizeOnDisk,
  } = item;

  const sceneSource = useSceneMetadataSource();
  const isScene = itemType === 'scene';

  // A scene links to the locked scene source its foreign id came from. Its
  // TPDB id is a scene id, so the TPDB movie link below is for movies only.
  const sceneLink =
    isScene && foreignId && sceneSource
      ? sceneSourceLink('scene', foreignId, sceneSource)
      : null;

  const handlePress = useCallback(() => {
    onPress(foreignId);
  }, [foreignId, onPress]);

  return (
    <div className={styles.container}>
      <Link className={styles.movie} component="div" onPress={handlePress}>
        <ImportMovieTitle
          itemType={itemType}
          title={title}
          year={year}
          releaseDate={releaseDate}
          studioTitle={studioTitle}
          performerNames={performerNames}
          searchCredits={searchCredits}
          isExistingMovie={isExisting}
          existingHasFile={hasFile}
          existingQuality={existingQuality}
          existingSize={sizeOnDisk}
        />
      </Link>

      {!isScene && !!tmdbId && (
        <Link
          className={styles.tmdbLink}
          to={`https://www.themoviedb.org/movie/${tmdbId}`}
        >
          <Icon
            className={styles.tmdbLinkIcon}
            name={icons.EXTERNAL_LINK}
            size={16}
          />
        </Link>
      )}

      {!isScene && !!tpdbId && (
        <Link
          className={styles.tpdbLink}
          to={`https://theporndb.net/movies/${tpdbId}`}
        >
          <Icon
            className={styles.tpdbLinkIcon}
            name={icons.EXTERNAL_LINK}
            size={16}
          />
        </Link>
      )}

      {sceneLink ? (
        <Link
          className={styles.stashdbLink}
          to={sceneLink.url}
          title={translateSourceLabel(sceneLink.label)}
        >
          <Icon
            className={styles.stashdbLinkIcon}
            name={icons.EXTERNAL_LINK}
            size={16}
          />
        </Link>
      ) : null}
    </div>
  );
}

export default ImportMovieSearchResult;
