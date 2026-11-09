import React, { useCallback, useRef } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import AddNewMovieSearchResult from './AddNewMovie/AddNewMovieSearchResult';
import { useAddNewMovie } from './useAddNewMovie';
import styles from './AddNewMovie.module.css';

function AddNewJav() {
  const {
    term,
    items,
    isFetching,
    error,
    colorImpairedMode,
    onLookupChange,
    onClearLookup,
  } = useAddNewMovie('jav');

  const searchInputRef = useRef<HTMLInputElement>(null);

  const onSearchInputChange = useCallback(
    ({ value }: { value: string }) => {
      onLookupChange(value);
    },
    [onLookupChange]
  );

  const onClearLookupPress = useCallback(() => {
    onClearLookup();
    searchInputRef.current?.focus();
  }, [onClearLookup]);

  return (
    <PageContent title={translate('Jav')}>
      <PageContentBody>
        <div className={styles.searchContainer}>
          <div className={styles.searchIconContainer}>
            <Icon name={icons.SEARCH} size={20} />
          </div>

          <TextInput
            ref={searchInputRef}
            className={styles.searchInput}
            name="javLookup"
            value={term}
            placeholder={translate('JavSearchPlaceholder')}
            autoFocus={true}
            onChange={onSearchInputChange}
          />

          <Button
            className={styles.clearLookupButton}
            onPress={onClearLookupPress}
          >
            <Icon name={icons.REMOVE} size={20} />
          </Button>
        </div>

        {isFetching && !items.length && <LoadingIndicator />}

        {!isFetching && !!error ? (
          <div className={styles.message}>
            <div className={styles.helpText}>
              {translate('FailedLoadingSearchResults')}
            </div>
            <Alert kind={kinds.WARNING}>
              {error?.statusBody?.message ?? error?.message ?? ''}
            </Alert>
          </div>
        ) : null}

        {!error && items.length > 0 ? (
          <div className={styles.searchResults}>
            {items.map((item) => (
              <AddNewMovieSearchResult
                key={item.foreignId}
                {...item}
                colorImpairedMode={colorImpairedMode}
              />
            ))}
          </div>
        ) : null}

        {!isFetching && !error && !items.length && !!term ? (
          <div className={styles.message}>
            <div className={styles.noResults}>
              {translate('CouldNotFindResults', { term })}
            </div>
          </div>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default AddNewJav;
