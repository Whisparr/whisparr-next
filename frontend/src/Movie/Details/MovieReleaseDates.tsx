import React from 'react';
import Icon from 'Components/Icon';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import { icons } from 'Helpers/Props';
import { useSceneMetadataSource } from 'Settings/MetadataSource/useMetadataSources';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import { sceneSourceLink } from 'Utilities/MetadataSource/sourceLinks';
import translate from 'Utilities/String/translate';
import Movie from '../Movie';
import styles from './MovieReleaseDates.module.css';

type MovieReleaseDatesProps = Pick<
  Movie,
  'foreignId' | 'releaseDate' | 'itemType'
>;

function MovieReleaseDates({
  foreignId,
  itemType,
  releaseDate,
}: Readonly<MovieReleaseDatesProps>) {
  const { showRelativeDates, shortDateFormat, timeFormat } =
    useUiSettingsValues();
  const sceneSource = useSceneMetadataSource();

  let url = `https://www.themoviedb.org/movie/${foreignId}`;

  if (itemType === 'movie' && foreignId.startsWith('tpdb:')) {
    url = `https://theporndb.net/movies/${foreignId}`;
  } else if (itemType !== 'movie') {
    // A scene's foreign id comes from the locked scene source.
    url = sceneSourceLink('scene', foreignId, sceneSource ?? 'stashDb').url;
  }

  if (!releaseDate) {
    return (
      <div>
        <div className={styles.dateIcon}>
          <Icon name={icons.MISSING} />
        </div>

        <InlineMarkdown
          data={translate('NoMovieReleaseDatesAvailable', { url })}
        />
      </div>
    );
  }

  return (
    <div>
      {releaseDate ? (
        <div title={translate('ReleaseDate')}>
          <div className={styles.dateIcon}>
            <Icon name={icons.CALENDAR} />
          </div>

          {getRelativeDate({
            date: releaseDate,
            shortDateFormat,
            showRelativeDates,
            timeFormat,
            timeForToday: false,
          })}
        </div>
      ) : null}
    </div>
  );
}

export default MovieReleaseDates;
