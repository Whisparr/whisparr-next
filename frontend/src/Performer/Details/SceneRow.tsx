import React, { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { useSafeForWorkMode } from 'App/safeForWorkStore';
import Icon from 'Components/Icon';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons } from 'Helpers/Props';
import MovieIndexProgressBar from 'Movie/Index/ProgressBar/MovieIndexProgressBar';
import Movie, { MovieStatus } from 'Movie/Movie';
import MovieSearchCell from 'Movie/MovieSearchCell';
import MovieTitleLink from 'Movie/MovieTitleLink';
import { useToggleMovieMonitored } from 'Movie/useMovie';
import { useSceneMetadataSource } from 'Settings/MetadataSource/useMetadataSources';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import formatRuntime from 'Utilities/Date/formatRuntime';
import { sceneSourceLink } from 'Utilities/MetadataSource/sourceLinks';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './SceneRow.module.css';

interface SceneRowProps {
  movie: Movie;
  isSaving?: boolean;
  columns: Column[];
  // From `useVisiblePerformerNames`, fetched once for the whole table
  visiblePerformerNames?: Record<string, string[]>;
}

export default function SceneRow(props: Readonly<SceneRowProps>) {
  const { movieRuntimeFormat } = useUiSettingsValues();
  const safeForWorkMode = useSafeForWorkMode();
  const sceneSource = useSceneMetadataSource();

  const { isSaving, columns, movie, visiblePerformerNames } = props;
  const {
    id,
    itemType,
    foreignId,
    monitored,
    performerForeignIds,
    performerNames,
    runtime,
    path,
    isAvailable,
    hasFile,
    movieFile,
    releaseDate,
    title,
    titleSlug,
    studioTitle,
    studioForeignId,
    sizeOnDisk,
  } = movie;

  const status = movie.status as MovieStatus;

  const externalLink = () => {
    if (!foreignId) return '';

    // A scene's foreign id is a UUID from the locked scene source. Check it
    // first: `parseInt` reads a UUID's leading digits as a TMDb id.
    if (itemType === 'scene') {
      return sceneSource
        ? sceneSourceLink('scene', foreignId, sceneSource).url
        : '';
    }

    if (foreignId.startsWith('tpdbid:')) {
      return `https://www.theporndb.net/movies/${foreignId.replace(
        'tpdbid:',
        ''
      )}`;
    }

    if (Number.parseInt(foreignId, 10) > 0) {
      return `https://www.themoviedb.org/movie/${foreignId.replace(
        'tmdbId:',
        ''
      )}`;
    }

    return '';
  };

  const { mutate: toggleMonitored } = useToggleMovieMonitored();

  // The names this scene's visible performers can be credited under. Names and
  // foreign ids aren't parallel lists, so match on name within this scene's ids.
  const visibleNames = useMemo(() => {
    if (!visiblePerformerNames) {
      return undefined;
    }

    const names = new Set<string>();

    performerForeignIds?.forEach((performerForeignId) => {
      visiblePerformerNames[performerForeignId]?.forEach((name) =>
        names.add(name.toLowerCase())
      );
    });

    return names;
  }, [visiblePerformerNames, performerForeignIds]);

  function onMonitorToggle(): void {
    toggleMonitored({ id: movie.id, monitored: !movie.monitored });
  }

  const url = externalLink();
  return (
    <TableRow>
      {columns.map((column) => {
        const { name, isVisible } = column;
        if (!isVisible) return null;

        if (name === 'monitored') {
          return (
            <TableRowCell key={name} className={styles.monitored}>
              <MonitorToggleButton
                className={styles.monitorToggleButton}
                size={20}
                type={itemType === 'scene' ? 'sceneMonitor' : 'movieMonitor'}
                monitored={monitored}
                moviesMonitored={monitored}
                isSaving={isSaving}
                isDisabled={false}
                onPress={onMonitorToggle}
              />
            </TableRowCell>
          );
        }

        if (name === 'title') {
          return (
            <TableRowCell key={name} className={styles.title}>
              <MovieTitleLink titleSlug={titleSlug} title={title} />
            </TableRowCell>
          );
        }

        // Fully populated Studio will link to it, otherwise just show name
        if (name === 'studioTitle') {
          const studioAnchor = (
            <span>
              {studioTitle}
              {url ? (
                <a href={url} target="_blank">
                  <Icon
                    className={styles.externalLink}
                    name={icons.EXTERNAL_LINK}
                  />
                </a>
              ) : null}
            </span>
          );

          let studioCell = studioAnchor;

          if (studioForeignId) {
            studioCell = (
              <Link to={`/studio/${studioForeignId}`}>{studioTitle}</Link>
            );
          } else if (sceneSource !== 'tpdb') {
            // The "add a link on stashdb.org" tip only applies to StashDB
            // scenes; ThePornDB scenes have no such step, so show no tip.
            studioCell = (
              <Tooltip
                anchor={studioAnchor}
                tooltip={translate('StudioNotInStashDb')}
              />
            );
          }

          return (
            <TableRowCell key={name} className={styles.studio}>
              {studioCell}
            </TableRowCell>
          );
        }

        if (name === 'releaseDate') {
          return <RelativeDateCell key={name} date={releaseDate} />;
        }

        if (name === 'credits' && performerNames) {
          // TODO: Workaround for duplicates before slicing
          const uniquePerformers = Array.from(new Set(performerNames))
            .sort((a, b) => (a > b ? 1 : -1))
            .slice(0, 4);
          const joinedPerformers = uniquePerformers.join(', ');
          return (
            <TableRowCell key={name} className={styles.performers}>
              <span title={joinedPerformers}>
                {uniquePerformers.map((performerName, index) => (
                  <React.Fragment key={performerName}>
                    {index > 0 ? ', ' : null}
                    <span
                      className={
                        visibleNames &&
                        !visibleNames.has(performerName.toLowerCase())
                          ? styles.hiddenPerformer
                          : undefined
                      }
                    >
                      {performerName}
                    </span>
                  </React.Fragment>
                ))}
              </span>
            </TableRowCell>
          );
        }

        if (name === 'runtime') {
          return (
            <TableRowCell key={name} className={styles.runtime}>
              {typeof runtime === 'number'
                ? formatRuntime(runtime, movieRuntimeFormat)
                : null}
            </TableRowCell>
          );
        }

        if (name === 'path') {
          return path ? (
            <TableRowCell
              key={name}
              className={safeForWorkMode ? styles.blurred : styles.path}
            >
              {path}
            </TableRowCell>
          ) : null;
        }

        if (name === 'sizeOnDisk') {
          return (
            <TableRowCell key={name} className={styles.size}>
              {sizeOnDisk ? formatBytes(sizeOnDisk) : null}
            </TableRowCell>
          );
        }

        if (name === 'status') {
          return (
            <TableRowCell key={name} className={styles.status}>
              <MovieIndexProgressBar
                movieId={id}
                isAvailable={isAvailable}
                hasFile={hasFile}
                movieFile={movieFile}
                monitored={monitored}
                detailedProgressBar={true}
                bottomRadius={false}
                isStandAlone={true}
                status={status}
                width={100}
              />
            </TableRowCell>
          );
        }

        if (name === 'actions') {
          return <MovieSearchCell key={name} movieId={id} />;
        }

        return null;
      })}
    </TableRow>
  );
}
