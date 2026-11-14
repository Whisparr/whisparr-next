import React, { useCallback, useState } from 'react';
import Icon from 'Components/Icon';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRowButton from 'Components/Table/TableRowButton';
import { icons } from 'Helpers/Props';
import { LogEventLevel } from 'typings/LogEvent';
import LogsTableDetailsModal from './LogsTableDetailsModal';
import styles from './LogsTableRow.module.css';

function getIconName(level: LogEventLevel) {
  switch (level) {
    case 'trace':
    case 'debug':
    case 'info':
      return icons.INFO;
    case 'warn':
      return icons.DANGER;
    case 'error':
      return icons.BUG;
    case 'fatal':
      return icons.FATAL;
    default:
      return icons.UNKNOWN;
  }
}

interface LogsTableRowProps {
  level: LogEventLevel;
  time: string;
  logger: string;
  message: string;
  exception?: string;
  columns: Column[];
}

function LogsTableRow({
  level,
  time,
  logger,
  message,
  exception,
  columns,
}: Readonly<LogsTableRowProps>) {
  const [isDetailsModalOpen, setIsDetailsModalOpen] = useState(false);

  const handlePress = useCallback(() => {
    setIsDetailsModalOpen(true);
  }, []);

  const handleModalClose = useCallback(() => {
    setIsDetailsModalOpen(false);
  }, []);

  // Modal sits outside the row: React bubbles portal clicks to ancestors, so the row's onPress would reopen it.
  return (
    <>
      <TableRowButton onPress={handlePress}>
        {columns.map((column) => {
          const { name, isVisible } = column;

          if (!isVisible) {
            return null;
          }

          if (name === 'level') {
            return (
              <TableRowCell key={name} className={styles.level}>
                <Icon
                  className={styles[level]}
                  name={getIconName(level)}
                  title={level}
                />
              </TableRowCell>
            );
          }

          if (name === 'time') {
            return <RelativeDateCell key={name} date={time} />;
          }

          if (name === 'logger') {
            return <TableRowCell key={name}>{logger}</TableRowCell>;
          }

          if (name === 'message') {
            return <TableRowCell key={name}>{message}</TableRowCell>;
          }

          if (name === 'actions') {
            return <TableRowCell key={name} className={styles.actions} />;
          }

          return null;
        })}
      </TableRowButton>

      <LogsTableDetailsModal
        isOpen={isDetailsModalOpen}
        message={message}
        exception={exception}
        onModalClose={handleModalClose}
      />
    </>
  );
}

export default LogsTableRow;
