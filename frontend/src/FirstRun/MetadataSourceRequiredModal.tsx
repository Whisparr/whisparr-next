import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import MetadataSourceRequiredModalContent from './MetadataSourceRequiredModalContent';

function onModalClose() {
  // No-op
}

interface MetadataSourceRequiredModalProps {
  isOpen: boolean;
}

export default function MetadataSourceRequiredModal({
  isOpen,
}: Readonly<MetadataSourceRequiredModalProps>) {
  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <MetadataSourceRequiredModalContent />
    </Modal>
  );
}
