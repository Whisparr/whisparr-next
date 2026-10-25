import React from 'react';
import translate from 'Utilities/String/translate';
import { SetupEnvTarget } from './useSetupStatus';
import styles from './SetupEnvTargets.module.css';

interface SetupEnvTargetsProps {
  targets: SetupEnvTarget[];
}

function SetupEnvTargets({ targets }: Readonly<SetupEnvTargetsProps>) {
  const hasMaskedPassword = targets.some((t) =>
    t.value.includes('Password=<unchanged>')
  );

  return (
    <div>
      <ul className={styles.targets}>
        {targets.map((target) => (
          <li key={target.name}>
            <code>
              {target.name}={target.value}
            </code>
          </li>
        ))}
      </ul>

      {hasMaskedPassword ? <p>{translate('SetupEnvPasswordKept')}</p> : null}
    </div>
  );
}

export default SetupEnvTargets;
