import React from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds, sizes } from 'Helpers/Props';
import MediaInfoType from 'typings/MediaInfo';
import translate from 'Utilities/String/translate';
import MediaInfo from './Editor/MediaInfo';
import { MatchTier } from './MovieFile';

const matchTierKeys: Record<MatchTier, string> = {
  unknown: 'MatchTierUnknown',
  name: 'MatchTierName',
  confirmed: 'MatchTierConfirmed',
  hash: 'MatchTierHash',
  manual: 'MatchTierManual',
  conflict: 'MatchTierConflict',
};

interface FileDetailsModalProps {
  isOpen: boolean;
  onModalClose: () => void;
  mediaInfo: MediaInfoType;
  matchTier?: MatchTier;
}

function FileDetailsModal({
  isOpen,
  onModalClose,
  mediaInfo,
  matchTier,
}: Readonly<FileDetailsModalProps>) {
  return (
    <Modal size={sizes.SMALL} isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{translate('Details')}</ModalHeader>
        <ModalBody>
          {matchTier ? (
            <DescriptionList>
              <DescriptionListItem
                title={translate('MatchTier')}
                data={
                  matchTier === 'conflict' ? (
                    <>
                      <Icon name={icons.WARNING} kind={kinds.WARNING} />{' '}
                      {translate(matchTierKeys.conflict)}
                    </>
                  ) : (
                    translate(matchTierKeys[matchTier])
                  )
                }
              />
            </DescriptionList>
          ) : null}

          <MediaInfo {...mediaInfo} />
        </ModalBody>
        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default FileDetailsModal;
