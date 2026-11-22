import React, { useCallback, useMemo } from 'react';
import { useAppDimension } from 'App/appStore';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import SpinnerIcon from 'Components/SpinnerIcon';
import Popover from 'Components/Tooltip/Popover';
import { align, icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import Movie from 'Movie/Movie';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { useStudioDetailsYearActions } from './useStudioDetailsYear';
import YearInfo from './YearInfo';
import styles from './StudioDetailsYearHeader.module.css';

interface StudioDetailsYearHeaderProps {
  studioId: number;
  year: number;
  works: Movie[];
  isExpanded: boolean;
  onExpandPress: (year: number, expand: boolean) => void;
  onYearRefreshPress?: (ids: number[]) => void;
}

function getMovieCountKind(
  missingMonitored: boolean,
  movieFileCount: number,
  totalMovieCount: number,
  monitoredMovieCount: number
) {
  switch (true) {
    case missingMonitored:
      return kinds.DANGER;
    case movieFileCount !== totalMovieCount &&
      !missingMonitored &&
      monitoredMovieCount > 0:
      return kinds.WARNING;
    case movieFileCount !== totalMovieCount &&
      !missingMonitored &&
      monitoredMovieCount === 0:
      return kinds.DEFAULT;
    case movieFileCount === totalMovieCount:
      return kinds.SUCCESS;
    default:
      return kinds.DANGER;
  }
}

function StudioDetailsYearHeader({
  studioId,
  year,
  works,
  isExpanded,
  onExpandPress,
  onYearRefreshPress,
}: Readonly<StudioDetailsYearHeaderProps>) {
  const isSmallScreen = useAppDimension('isSmallScreen');
  const isSearching = false;

  const {
    totalMovieCount,
    monitoredMovieCount,
    movieFileCount,
    missingMonitored,
    sizeOnDisk,
  } = useMemo(
    () => ({
      totalMovieCount: works.length,
      monitoredMovieCount: works.filter((m) => m.monitored).length,
      movieFileCount: works.filter((m) => m.sizeOnDisk && m.sizeOnDisk > 0)
        .length,
      missingMonitored: works.some((m) => m.monitored && !m.movieFileId),
      sizeOnDisk: works.reduce(
        (total, movie) => total + (movie.sizeOnDisk || 0),
        0
      ),
    }),
    [works]
  );

  const { onMonitorYearPress, onSearchPress } = useStudioDetailsYearActions(
    studioId,
    year,
    works
  );

  const handleExpandPress = useCallback(() => {
    onExpandPress(year, !isExpanded);
  }, [isExpanded, onExpandPress, year]);

  const handleRefreshPress = useCallback(() => {
    onYearRefreshPress?.(works.map((item) => item.id));
  }, [onYearRefreshPress, works]);

  const yearKind = getMovieCountKind(
    missingMonitored,
    movieFileCount,
    totalMovieCount,
    monitoredMovieCount
  );

  return (
    <div className={styles.header}>
      <div className={styles.left}>
        <MonitorToggleButton
          monitored={monitoredMovieCount === totalMovieCount}
          size={24}
          onPress={onMonitorYearPress}
        />
        <span className={styles.seasonNumber}>{year}</span>
        <Popover
          className={styles.movieCountTooltip}
          canFlip={true}
          anchor={
            <Label kind={yearKind} size={sizes.LARGE}>
              <span>
                {movieFileCount} / {totalMovieCount}
              </span>
            </Label>
          }
          title={translate('SeasonInformation')}
          body={
            <div>
              <YearInfo
                totalMovieCount={totalMovieCount}
                monitoredMovieCount={monitoredMovieCount}
                movieFileCount={movieFileCount}
                sizeOnDisk={sizeOnDisk}
              />
            </div>
          }
          position={tooltipPositions.BOTTOM}
        />
        {sizeOnDisk ? (
          <div className={styles.sizeOnDisk}>{formatBytes(sizeOnDisk)}</div>
        ) : null}
      </div>
      <Link className={styles.expandButton} onPress={handleExpandPress}>
        <Icon
          className={styles.expandButtonIcon}
          name={isExpanded ? icons.COLLAPSE : icons.EXPAND}
          title={isExpanded ? translate('HideMovies') : translate('ShowMovies')}
          size={24}
        />
        {!isSmallScreen && <span>&nbsp;</span>}
      </Link>
      {isSmallScreen ? (
        <Menu className={styles.actionsMenu} alignMenu={align.RIGHT}>
          <MenuButton>
            <Icon name={icons.ACTIONS} size={22} />
          </MenuButton>
          <MenuContent className={styles.actionsMenuContent}>
            <MenuItem onPress={handleRefreshPress}>
              <Icon className={styles.actionMenuIcon} name={icons.REFRESH} />
              {translate('Refresh')}
            </MenuItem>
            <MenuItem
              isDisabled={isSearching || monitoredMovieCount === 0}
              onPress={onSearchPress}
            >
              <SpinnerIcon
                className={styles.actionMenuIcon}
                name={icons.SEARCH}
                isSpinning={isSearching}
              />
              {translate('Search')}
            </MenuItem>
          </MenuContent>
        </Menu>
      ) : (
        <div className={styles.actions}>
          <SpinnerIconButton
            className={styles.actionButton}
            name={icons.REFRESH}
            title={translate('Refresh')}
            size={24}
            isSpinning={false}
            isDisabled={false}
            onPress={handleRefreshPress}
          />
          <SpinnerIconButton
            className={styles.actionButton}
            name={icons.SEARCH}
            title={
              monitoredMovieCount > 0
                ? translate('SearchForMonitoredMoviesYear')
                : translate('NoMonitoredMoviesYear')
            }
            size={24}
            isSpinning={isSearching}
            isDisabled={isSearching || monitoredMovieCount === 0}
            onPress={onSearchPress}
          />
        </div>
      )}
    </div>
  );
}

export default React.memo(StudioDetailsYearHeader);
