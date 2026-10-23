import React, { ReactNode } from 'react';
import Link, { LinkProps } from 'Components/Link/Link';
import TableRowCell from './TableRowCell';
import styles from './TableRowCellButton.module.css';

interface TableRowCellButtonProps extends LinkProps {
  className?: string;
  children: ReactNode;
  // Rendered beside the button (not inside it), above the button's
  // cell-covering hit area, for controls that must stay independent of it.
  after?: ReactNode;
}

function TableRowCellButton(props: Readonly<TableRowCellButtonProps>) {
  const {
    className = styles.cell,
    children,
    title,
    after,
    ...otherProps
  } = props;

  return (
    <TableRowCell className={className}>
      <Link
        className={styles.button}
        title={title}
        aria-label={title}
        {...otherProps}
      >
        {children}
      </Link>

      {after ? <span className={styles.after}>{after}</span> : null}
    </TableRowCell>
  );
}

export default TableRowCellButton;
