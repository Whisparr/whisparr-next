import moment from 'moment';
import React from 'react';
import { CommandProgress } from 'Commands/Command';
import ProgressBar from 'Components/ProgressBar';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import formatTimeSpan from 'Utilities/Date/formatTimeSpan';
import formatNumber from 'Utilities/Number/formatNumber';
import translate from 'Utilities/String/translate';
import styles from './QueuedTaskRowNameCell.module.css';

const MIN_ELAPSED_FOR_ETA_MS = 60 * 1000;

export interface QueuedTaskRowNameCellProps {
  commandName: string;
  message?: string;
  clientUserAgent?: string;
  status?: string;
  started?: string;
  progress?: CommandProgress;
}

function getEta(started: string | undefined, percent: number) {
  if (!started || percent <= 0) {
    return null;
  }

  const elapsed = moment().diff(moment(started));

  if (elapsed < MIN_ELAPSED_FOR_ETA_MS) {
    return null;
  }

  return formatTimeSpan((elapsed * (100 - percent)) / percent);
}

export default function QueuedTaskRowNameCell(
  props: Readonly<QueuedTaskRowNameCellProps>
) {
  const { commandName, message, clientUserAgent, status, started, progress } =
    props;

  // Re-rendered on every progress push from the server, so the ETA stays current
  const showProgress = status === 'started' && !!progress;
  const eta = showProgress ? getEta(started, progress.percent) : null;

  return (
    <TableRowCell>
      <span className={styles.commandName}>
        {commandName}
        {message ? <span> - {message}</span> : null}
      </span>

      {clientUserAgent ? (
        <span
          className={styles.userAgent}
          title={translate('TaskUserAgentTooltip')}
        >
          {translate('From')}: {clientUserAgent}
        </span>
      ) : null}

      {showProgress ? (
        <div className={styles.progress}>
          <ProgressBar progress={progress.percent} size="small" />

          <div className={styles.progressText}>
            {translate('CommandProgress', {
              phase: progress.phase,
              done: formatNumber(progress.done),
              total: formatNumber(progress.total),
              percent: progress.percent.toFixed(1),
            })}

            {eta ? ` - ${translate('CommandProgressEta', { eta })}` : null}
          </div>
        </div>
      ) : null}
    </TableRowCell>
  );
}
