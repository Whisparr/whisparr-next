import React, { useCallback, useState } from 'react';
import { useAddMovieDefaults } from 'AddMovie/addMovieDefaultsStore';
import Alert from 'Components/Alert';
import EnhancedSelectInput, {
  EnhancedSelectInputValue,
} from 'Components/Form/Select/EnhancedSelectInput';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import useDebounce from 'Helpers/Hooks/useDebounce';
import { icons, kinds } from 'Helpers/Props';
import {
  useJavAvailable,
  useSceneMetadataSource,
} from 'Settings/MetadataSource/useMetadataSources';
import { useQualityProfiles } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import { itemSourceLink } from 'Utilities/MetadataSource/sourceLinks';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import UnmappedFileMatchLabels from './UnmappedFileMatchLabels';
import {
  UnmappedFile,
  UnmappedFileSuggestion,
  UnmappedItemType,
  useLinkUnmappedFile,
  useUnmappedFileSuggestions,
} from './useUnmappedFiles';
import styles from './UnmappedFileLinkModal.module.css';

const ITEM_TYPE_OPTIONS: EnhancedSelectInputValue<UnmappedItemType>[] = [
  {
    key: 'scene',
    get value() {
      return translate('Scene');
    },
  },
  {
    key: 'movie',
    get value() {
      return translate('Movie');
    },
  },
];

const JAV_ITEM_TYPE_OPTIONS: EnhancedSelectInputValue<UnmappedItemType>[] = [
  ...ITEM_TYPE_OPTIONS,
  {
    key: 'jav',
    get value() {
      return translate('Jav');
    },
  },
];

interface ResultProps {
  readonly result: UnmappedFileSuggestion;
  readonly isLinking: boolean;
  readonly onLinkPress: (result: UnmappedFileSuggestion) => void;
}

function Result({ result, isLinking, onLinkPress }: ResultProps) {
  const sceneSource = useSceneMetadataSource();
  const sourceLink = itemSourceLink(
    result.itemType,
    result.foreignId,
    sceneSource
  );

  const handlePress = useCallback(() => {
    onLinkPress(result);
  }, [result, onLinkPress]);

  return (
    <div className={styles.result}>
      <div className={styles.details}>
        <div className={styles.title}>
          {result.title}

          {sourceLink ? (
            <Link
              className={styles.sourceLink}
              to={sourceLink.url}
              title={sourceLink.name}
            >
              <Icon name={icons.EXTERNAL_LINK} size={12} />
            </Link>
          ) : null}
        </div>

        <div className={styles.subtitle}>
          {[result.code, result.studioTitle, result.releaseDate?.slice(0, 10)]
            .filter(Boolean)
            .join(' · ')}
        </div>

        <div className={styles.labels}>
          <UnmappedFileMatchLabels match={result} />

          <Label>
            {translate('TitleSimilarity', { percent: result.titleSimilarity })}
          </Label>

          {result.hasFile ? (
            <Label kind={kinds.WARNING}>{translate('AlreadyHasFile')}</Label>
          ) : null}
        </div>
      </div>

      <SpinnerButton
        isSpinning={isLinking}
        isDisabled={result.hasFile}
        onPress={handlePress}
      >
        {translate('Link')}
      </SpinnerButton>
    </div>
  );
}

interface UnmappedFileLinkModalProps {
  readonly file: UnmappedFile;
  readonly defaultItemType: UnmappedItemType;
  readonly isOpen: boolean;
  readonly onModalClose: () => void;
}

function UnmappedFileLinkModal({
  file,
  defaultItemType,
  isOpen,
  onModalClose,
}: UnmappedFileLinkModalProps) {
  const [term, setTerm] = useState<string | null>(null);
  const [itemType, setItemType] = useState<UnmappedItemType>(defaultItemType);
  const debouncedTerm = useDebounce(term ?? '', 400);

  // Nothing is searched until the modal opens, and the file's own title is used until something is typed
  const { data, isFetching, error } = useUnmappedFileSuggestions(
    file.id,
    debouncedTerm,
    itemType,
    isOpen
  );

  const javAvailable = useJavAvailable();
  const { qualityProfileId, monitor } = useAddMovieDefaults();
  const { data: qualityProfiles } = useQualityProfiles();
  const link = useLinkUnmappedFile();
  const linkError = link.error ? getErrorMessage(link.error) : null;

  const handleTermChange = useCallback(({ value }: InputChanged<string>) => {
    setTerm(value);
  }, []);

  const handleItemTypeChange = useCallback(
    ({ value }: InputChanged<UnmappedItemType>) => {
      setItemType(value);
    },
    []
  );

  const handleLinkPress = useCallback(
    (result: UnmappedFileSuggestion) => {
      link.mutate(
        {
          id: file.id,
          foreignId: result.foreignId,
          itemType: result.itemType,
          qualityProfileId: qualityProfileId || qualityProfiles[0]?.id,
          monitored: monitor === 'movieOnly',
        },
        { onSuccess: onModalClose }
      );
    },
    [file.id, link, monitor, onModalClose, qualityProfileId, qualityProfiles]
  );

  const results = data?.results ?? [];

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{translate('UnmappedFileLink')}</ModalHeader>

        <ModalBody>
          <div className={styles.path}>{file.path}</div>

          <div className={styles.controls}>
            <TextInput
              className={styles.term}
              name="term"
              value={term ?? data?.term ?? ''}
              onChange={handleTermChange}
            />

            <EnhancedSelectInput
              name="itemType"
              value={itemType}
              values={javAvailable ? JAV_ITEM_TYPE_OPTIONS : ITEM_TYPE_OPTIONS}
              onChange={handleItemTypeChange}
            />
          </div>

          {linkError ? <Alert kind={kinds.DANGER}>{linkError}</Alert> : null}

          {error ? (
            <Alert kind={kinds.DANGER}>{translate('SearchFailedError')}</Alert>
          ) : null}

          {isFetching ? <LoadingIndicator /> : null}

          {!isFetching && !error && !results.length ? (
            <Alert kind={kinds.INFO}>{translate('NoResultsFound')}</Alert>
          ) : null}

          {isFetching
            ? null
            : results.map((result) => (
                <Result
                  key={result.foreignId}
                  result={result}
                  isLinking={link.isPending}
                  onLinkPress={handleLinkPress}
                />
              ))}
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default UnmappedFileLinkModal;
