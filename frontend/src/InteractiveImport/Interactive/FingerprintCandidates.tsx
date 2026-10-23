import React, { useCallback, useMemo } from 'react';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import Popover from 'Components/Tooltip/Popover';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import Movie from 'Movie/Movie';
import { useMoviesByIds } from 'Movie/useMovie';
import FingerprintCandidate from 'typings/FingerprintCandidate';
import translate from 'Utilities/String/translate';
import styles from './FingerprintCandidates.module.css';

interface FingerprintCandidateItemProps {
  candidate: FingerprintCandidate;
  movie?: Movie;
  onUsePress(movie: Movie): void;
}

function FingerprintCandidateItem({
  candidate,
  movie,
  onUsePress,
}: Readonly<FingerprintCandidateItemProps>) {
  const handleUsePress = useCallback(() => {
    if (movie) {
      onUsePress(movie);
    }
  }, [movie, onUsePress]);

  const details = [candidate.studioTitle, candidate.releaseDate?.slice(0, 10)]
    .filter(Boolean)
    .join(' - ');

  return (
    <li className={styles.candidate}>
      <div className={styles.info}>
        <div className={styles.title}>
          {candidate.title ?? candidate.sceneId}
        </div>

        {details ? <div className={styles.details}>{details}</div> : null}

        <div className={styles.meta}>
          <Label size={sizes.SMALL}>{candidate.algorithm}</Label>
          <span>{`d${candidate.distance}`}</span>
          <span>{`x${candidate.submissions}`}</span>
        </div>
      </div>

      {candidate.movieId == null ? (
        <Label kind={kinds.WARNING}>{translate('NotInLibrary')}</Label>
      ) : (
        <Button isDisabled={!movie} onPress={handleUsePress}>
          {translate('UseScene')}
        </Button>
      )}
    </li>
  );
}

interface FingerprintCandidatesProps {
  candidates: FingerprintCandidate[];
  onMovieSelect(movie: Movie): void;
}

interface FingerprintCandidateListProps extends FingerprintCandidatesProps {
  libraryIds: number[];
}

// Rendered only while the popover is open, so the library lookup for the
// in-library candidates happens on demand rather than once per row.
function FingerprintCandidateList({
  candidates,
  libraryIds,
  onMovieSelect,
}: Readonly<FingerprintCandidateListProps>) {
  const { movies } = useMoviesByIds(libraryIds);

  return (
    <ul className={styles.list}>
      {candidates.map((candidate) => (
        <FingerprintCandidateItem
          key={`${candidate.algorithm}-${candidate.sceneId}`}
          candidate={candidate}
          movie={movies.find((m) => m.id === candidate.movieId)}
          onUsePress={onMovieSelect}
        />
      ))}
    </ul>
  );
}

function FingerprintCandidates({
  candidates,
  onMovieSelect,
}: Readonly<FingerprintCandidatesProps>) {
  const libraryIds = useMemo(
    () =>
      candidates
        .map((c) => c.movieId)
        .filter((movieId): movieId is number => movieId != null),
    [candidates]
  );

  return (
    <Popover
      anchor={<Icon name={icons.SEARCH} kind={kinds.INFO} />}
      isAnchorFocusable={false}
      title={translate('FingerprintCandidates')}
      body={
        <FingerprintCandidateList
          candidates={candidates}
          libraryIds={libraryIds}
          onMovieSelect={onMovieSelect}
        />
      }
      position={tooltipPositions.LEFT}
      canFlip={false}
    />
  );
}

export default FingerprintCandidates;
