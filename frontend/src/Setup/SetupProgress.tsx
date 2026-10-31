import React from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import SetupShutDown from './SetupShutDown';
import { SetupProgressStatus } from './useSetupStatus';
import styles from './SetupProgress.module.css';

interface SetupProgressProps {
  progress: SetupProgressStatus;
  isDocker: boolean;
  onRetry: () => void;
  onShutDown: () => void;
}

const stateClasses: Record<string, string> = {
  pending: styles.pending,
  running: styles.running,
  done: styles.done,
  failed: styles.failed,
  skipped: styles.skipped,
};

function capitalize(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function SetupProgress({
  progress,
  isDocker,
  onRetry,
  onShutDown,
}: Readonly<SetupProgressProps>) {
  const isFailed = progress.status === 'failed';
  const isRestarting = progress.status === 'restarting';

  return (
    <div>
      <ul className={styles.steps}>
        {progress.steps.map((step) => (
          <li
            key={step.step}
            className={stateClasses[step.state] ?? styles.pending}
          >
            <span className={styles.state}>
              {translate(`SetupState${capitalize(step.state)}`)}
            </span>
            {translate(`SetupStep${capitalize(step.step)}`)}
          </li>
        ))}
      </ul>

      {progress.backupPath ? (
        <p>{translate('SetupBackupPath', { path: progress.backupPath })}</p>
      ) : null}

      {progress.skippedMovies > 0 ? (
        <div>
          <p>
            {translate('SetupSkippedMovies', { count: progress.skippedMovies })}
          </p>

          <ul className={styles.paths}>
            {progress.skippedMoviePaths.map((path) => (
              <li key={path}>
                <code>{path}</code>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {isFailed ? (
        <div className={styles.failure}>
          <h3>{translate('SetupMigrationFailed')}</h3>
          <p>{progress.errorMessage}</p>

          <div className={styles.buttons}>
            {progress.canRetry ? (
              <Button kind={kinds.PRIMARY} onPress={onRetry}>
                {translate('SetupRetry')}
              </Button>
            ) : null}

            <SetupShutDown isDocker={isDocker} onPress={onShutDown} />
          </div>
        </div>
      ) : null}

      {isRestarting ? <p>{translate('SetupRestarting')}</p> : null}
    </div>
  );
}

export default SetupProgress;
