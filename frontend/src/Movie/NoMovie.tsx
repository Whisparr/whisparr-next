import React from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './NoMovie.module.css';

interface NoMovieProps {
  totalItems: number;
  itemType?: 'movie' | 'jav';
}

function NoMovie(props: Readonly<NoMovieProps>) {
  const { totalItems, itemType = 'movie' } = props;

  if (totalItems > 0) {
    return (
      <div>
        <div className={styles.message}>
          {translate('AllMoviesHiddenDueToFilter')}
        </div>
      </div>
    );
  }

  if (itemType === 'jav') {
    return (
      <div>
        <div className={styles.message}>{translate('NoJav')}</div>

        <div className={styles.buttonContainer}>
          <Button to="/add/new/jav" kind={kinds.PRIMARY}>
            {translate('AddNew')}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className={styles.message}>{translate('NoMoviesExist')}</div>

      <div className={styles.buttonContainer}>
        <Button to="/add/import/movies" kind={kinds.PRIMARY}>
          {translate('ImportExistingMovies')}
        </Button>
      </div>

      <div className={styles.buttonContainer}>
        <Button to="/add/new/movie" kind={kinds.PRIMARY}>
          {translate('AddNewMovie')}
        </Button>
      </div>
    </div>
  );
}

export default NoMovie;
