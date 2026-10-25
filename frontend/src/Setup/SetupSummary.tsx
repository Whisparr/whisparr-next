import React from 'react';
import translate from 'Utilities/String/translate';
import { LegacyKind, SetupInstall } from './useSetupStatus';
import styles from './SetupSummary.module.css';

interface SetupSummaryProps {
  installs: SetupInstall[];
  kind: LegacyKind;
  onKindChange: (kind: LegacyKind) => void;
}

function appName(kind: LegacyKind) {
  return translate(kind === 'eros' ? 'SetupAppEros' : 'SetupAppV2');
}

function SetupSummary({
  installs,
  kind,
  onKindChange,
}: Readonly<SetupSummaryProps>) {
  const install = installs.find((i) => i.kind === kind);

  return (
    <div className={styles.summary}>
      {installs.length > 1 ? (
        <fieldset className={styles.choice}>
          <legend>{translate('SetupKindChoice')}</legend>

          {installs.map((i) => (
            <label key={i.kind} className={styles.option}>
              <input
                type="radio"
                name="setupKind"
                value={i.kind}
                checked={i.kind === kind}
                onChange={() => onKindChange(i.kind)}
              />
              {appName(i.kind)}
            </label>
          ))}
        </fieldset>
      ) : null}

      {install ? (
        <p className={styles.found}>
          {translate('SetupFoundSummary', {
            app: appName(install.kind),
            database: translate(
              install.databaseType.toLowerCase() === 'postgresql'
                ? 'SetupDatabasePostgreSQL'
                : 'SetupDatabaseSQLite'
            ),
            studios: install.studios,
            scenes: install.scenes,
            movies: install.movies,
            files: install.files,
          })}
        </p>
      ) : null}

      <ul className={styles.notices}>
        <li>{translate('SetupNoticeBackup')}</li>
        <li>{translate('SetupNoticeNothingMoves')}</li>
        <li>{translate('SetupNoticeRenaming')}</li>
      </ul>
    </div>
  );
}

export default SetupSummary;
