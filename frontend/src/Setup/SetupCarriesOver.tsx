import React from 'react';
import translate from 'Utilities/String/translate';
import { LegacyKind } from './useSetupStatus';
import styles from './SetupCarriesOver.module.css';

interface SetupCarriesOverProps {
  kind: LegacyKind;
  // Eros keeps its whole database only when the source stays the same.
  sameSource: boolean;
}

function dispositionRows(kind: LegacyKind, sameSource: boolean) {
  if (kind === 'v2') {
    return [
      ['SetupDispositionCopy', 'SetupDispositionCopyV2'],
      ['SetupDispositionPort', 'SetupDispositionPortV2'],
      ['SetupDispositionStream', 'SetupDispositionStreamV2'],
      ['SetupDispositionLeave', 'SetupDispositionLeaveV2'],
    ];
  }

  if (sameSource) {
    return [
      ['SetupDispositionCopy', 'SetupDispositionCopyErosSameSource'],
      ['SetupDispositionPort', 'SetupDispositionNothing'],
      ['SetupDispositionStream', 'SetupDispositionNothing'],
      ['SetupDispositionLeave', 'SetupDispositionLeaveErosSameSource'],
    ];
  }

  return [
    ['SetupDispositionCopy', 'SetupDispositionCopyErosNewSource'],
    ['SetupDispositionPort', 'SetupDispositionPortErosNewSource'],
    ['SetupDispositionStream', 'SetupDispositionStreamErosNewSource'],
    ['SetupDispositionLeave', 'SetupDispositionLeaveErosNewSource'],
  ];
}

function SetupCarriesOver({
  kind,
  sameSource,
}: Readonly<SetupCarriesOverProps>) {
  return (
    <details className={styles.carriesOver}>
      <summary>{translate('SetupCarriesOver')}</summary>

      <table className={styles.table}>
        <tbody>
          {dispositionRows(kind, sameSource).map(([label, text]) => (
            <tr key={label}>
              <th>{translate(label)}</th>
              <td>{translate(text)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {kind === 'v2' ? <p>{translate('SetupV2QueueNote')}</p> : null}

      <h4>{translate('SetupResetTitle')}</h4>
      <p>{translate('SetupResetSteps')}</p>

      <h4>{translate('SetupDowngradeTitle')}</h4>
      <p>{translate('SetupDowngradeSteps')}</p>
    </details>
  );
}

export default SetupCarriesOver;
