import React, { useEffect } from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import { useJavAvailable } from 'Settings/MetadataSource/useMetadataSources';
import useSystemStatus from 'System/Status/useSystemStatus';
import formatNumber from 'Utilities/Number/formatNumber';
import translate from 'Utilities/String/translate';
import styles from './Stats.module.css';

function Stats() {
  const { data, refetch } = useSystemStatus();
  const javAvailable = useJavAvailable();

  const { movieCount, sceneCount, javCount, studioCount, performerCount } =
    data;

  useEffect(() => {
    refetch();
  }, [refetch]);

  return (
    <FieldSet legend={translate('Stats')}>
      <DescriptionList className={styles.descriptionList}>
        <DescriptionListItem
          title={translate('SceneCount')}
          data={formatNumber(sceneCount)}
        />

        <DescriptionListItem
          title={translate('MovieCount')}
          data={formatNumber(movieCount)}
        />

        {javAvailable ? (
          <DescriptionListItem
            title={translate('JavCount')}
            data={formatNumber(javCount)}
          />
        ) : null}

        <DescriptionListItem
          title={translate('StudioCount')}
          data={formatNumber(studioCount)}
        />

        <DescriptionListItem
          title={translate('PerformerCount')}
          data={formatNumber(performerCount)}
        />
      </DescriptionList>
    </FieldSet>
  );
}

export default Stats;
