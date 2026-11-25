import React from 'react';
import { useModalContext } from './ModalContext';
import styles from './ModalHeader.module.css';

interface ModalHeaderProps extends React.HTMLAttributes<HTMLDivElement> {
  children?: React.ReactNode;
  subtitle?: React.ReactNode;
}

function ModalHeader({
  children,
  subtitle,
  ...otherProps
}: Readonly<ModalHeaderProps>) {
  const { headerId } = useModalContext();

  return (
    <div id={headerId} className={styles.modalHeader} {...otherProps}>
      {children}
      {subtitle ? <span className={styles.subtitle}>{subtitle}</span> : null}
    </div>
  );
}

export default ModalHeader;
