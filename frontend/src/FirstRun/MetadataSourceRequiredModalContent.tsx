import React, { ChangeEvent, useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormLabel from 'Components/Form/FormLabel';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import {
  getMovieSourceOptions,
  getSceneSourceOptions,
} from 'Settings/MetadataSource/metadataSourceOptions';
import {
  MetadataSourcesChoice,
  useChooseMetadataSources,
} from 'Settings/MetadataSource/useMetadataSources';
import translate from 'Utilities/String/translate';
import styles from './MetadataSourceRequiredModalContent.module.css';

type SceneSource = MetadataSourcesChoice['sceneMetadataSource'];
type MovieSource = MetadataSourcesChoice['movieMetadataSource'];

function onModalClose() {
  // No-op
}

export default function MetadataSourceRequiredModalContent() {
  const { choose, isSaving, error } = useChooseMetadataSources();

  const [sceneSource, setSceneSource] = useState<SceneSource>('stashDb');
  const [movieSource, setMovieSource] = useState<MovieSource>('tmdb');

  const handleSceneSourceChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      const value = event.target.value as SceneSource;

      setSceneSource(value);

      // TPDB scenes pair best with TPDB movies; preselect it, but leave the
      // user free to change it back.
      if (value === 'tpdb') {
        setMovieSource('tpdb');
      }
    },
    []
  );

  const handleMovieSourceChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      setMovieSource(event.target.value as MovieSource);
    },
    []
  );

  const handleSavePress = useCallback(() => {
    choose({
      sceneMetadataSource: sceneSource,
      movieMetadataSource: movieSource,
    });
  }, [choose, sceneSource, movieSource]);

  return (
    <ModalContent showCloseButton={false} onModalClose={onModalClose}>
      <ModalHeader>{translate('ChooseMetadataSources')}</ModalHeader>

      <ModalBody>
        <div className={styles.helpText}>
          {translate('ChooseMetadataSourcesHelpText')}
        </div>

        <FormGroup>
          <FormLabel>{translate('SceneMetadataSource')}</FormLabel>

          <fieldset
            className={styles.radioGroup}
            aria-label={translate('SceneMetadataSource')}
          >
            {getSceneSourceOptions().map(({ key, label }) => (
              <label key={key} className={styles.radioOption}>
                <input
                  type="radio"
                  name="sceneMetadataSource"
                  value={key}
                  checked={sceneSource === key}
                  onChange={handleSceneSourceChange}
                />
                {label}
              </label>
            ))}
          </fieldset>
        </FormGroup>

        {sceneSource === 'tpdb' ? (
          <Alert className={styles.alert} kind={kinds.INFO}>
            {translate('TpdbScenesEncourageTpdbMovies')}
          </Alert>
        ) : null}

        <FormGroup>
          <FormLabel>{translate('WhisparrMovieMetadataSource')}</FormLabel>

          <fieldset
            className={styles.radioGroup}
            aria-label={translate('WhisparrMovieMetadataSource')}
          >
            {getMovieSourceOptions().map(({ key, label }) => (
              <label key={key} className={styles.radioOption}>
                <input
                  type="radio"
                  name="movieMetadataSource"
                  value={key}
                  checked={movieSource === key}
                  onChange={handleMovieSourceChange}
                />
                {label}
              </label>
            ))}
          </fieldset>
        </FormGroup>

        <Alert className={styles.permanentAlert} kind={kinds.WARNING}>
          {translate('MetadataSourceChoiceIsPermanent')}
        </Alert>
      </ModalBody>

      <ModalFooter>
        <SpinnerErrorButton
          kind={kinds.PRIMARY}
          isSpinning={isSaving}
          error={error}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}
