import React from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import SetupEnvTargets from './SetupEnvTargets';
import SetupShutDown from './SetupShutDown';
import {
  MovieSource,
  SceneSource,
  SetupChoice,
  SetupEnvTarget,
  SetupInstall,
} from './useSetupStatus';
import styles from './SetupSourceForm.module.css';

interface SetupSourceFormProps {
  install: SetupInstall;
  choice: SetupChoice;
  envTargets: SetupEnvTarget[];
  isBusy: boolean;
  isDocker: boolean;
  submitError: string | null;
  onChange: (choice: SetupChoice) => void;
  onMigrate: () => void;
  onShutDown: () => void;
}

const sceneOptions: { value: SceneSource; label: string }[] = [
  { value: 'stashDb', label: 'StashDB' },
  { value: 'tpdb', label: 'TPDB' },
];

const movieOptions: { value: MovieSource; label: string }[] = [
  { value: 'tmdb', label: 'TMDB' },
  { value: 'tpdb', label: 'TPDB' },
];

function SetupSourceForm({
  install,
  choice,
  envTargets,
  isBusy,
  isDocker,
  submitError,
  onChange,
  onMigrate,
  onShutDown,
}: Readonly<SetupSourceFormProps>) {
  const sceneIsPrevious = choice.sceneSource === install.previousSceneSource;
  const movieIsPrevious = choice.movieSource === install.previousMovieSource;

  return (
    <div>
      <div className={styles.field}>
        <label htmlFor="setupSceneSource">
          {translate('SetupSceneSource')}
        </label>

        <select
          id="setupSceneSource"
          value={choice.sceneSource}
          onChange={(e) =>
            onChange({ ...choice, sceneSource: e.target.value as SceneSource })
          }
        >
          {sceneOptions.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>

        <span className={sceneIsPrevious ? styles.good : styles.slow}>
          {translate(
            sceneIsPrevious ? 'SetupPreviousSource' : 'SetupRebuildsFromFiles'
          )}
        </span>
      </div>

      <div className={styles.field}>
        <label htmlFor="setupMovieSource">
          {translate('SetupMovieSource')}
        </label>

        <select
          id="setupMovieSource"
          value={choice.movieSource}
          onChange={(e) =>
            onChange({ ...choice, movieSource: e.target.value as MovieSource })
          }
        >
          {movieOptions.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>

        <span className={movieIsPrevious ? styles.good : styles.slow}>
          {translate(
            movieIsPrevious ? 'SetupPreviousSource' : 'SetupRebuildsFromFiles'
          )}
        </span>

        {choice.sceneSource === 'tpdb' && choice.movieSource === 'tmdb' ? (
          <span className={styles.warning}>
            {translate('SetupTpdbMoviesRecommended')}
          </span>
        ) : null}
      </div>

      <p className={styles.locked}>{translate('SetupChoiceLocked')}</p>

      {envTargets.length > 0 ? (
        <div className={styles.envNotice}>
          <p>{translate('SetupEnvBeforeMigrating')}</p>

          <SetupEnvTargets targets={envTargets} />
        </div>
      ) : null}

      {submitError ? <p className={styles.error}>{submitError}</p> : null}

      <div className={styles.buttons}>
        <Button kind={kinds.PRIMARY} isDisabled={isBusy} onPress={onMigrate}>
          {translate('SetupBackUpAndMigrate')}
        </Button>

        <SetupShutDown isDocker={isDocker} onPress={onShutDown} />
      </div>
    </div>
  );
}

export default SetupSourceForm;
