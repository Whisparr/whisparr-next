import { useSortable } from '@dnd-kit/react/sortable';
import classNames from 'classnames';
import React, { useCallback } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import IconButton from 'Components/Link/IconButton';
import SortableHandle, {
  SortableHandleOption,
} from 'Components/Sortable/SortableHandle';
import { icons } from 'Helpers/Props';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import type { QualityProfileMoveTarget } from './QualityProfileItems';
import { qualityKey, ROOT_CONTAINER } from './useQualityProfileDnd';
import styles from './QualityProfileItem.module.css';

export interface QualityProfileItemProps {
  editGroups?: boolean;
  // The sortable container this quality sits in: the root list or a group.
  containerId: string;
  index: number;
  groupId?: number;
  qualityId: number;
  name: string;
  allowed: boolean;
  isFirst: boolean;
  isLast: boolean;
  moveTargets: QualityProfileMoveTarget[];
  onCreateGroupPress?: (qualityId: number) => void;
  onQualityProfileItemAllowedChange?: (
    qualityId: number,
    allowed: boolean
  ) => void;
  onMoveItem: (itemId: string, containerId: string, offset: number) => void;
  onMoveItemTo: (
    itemId: string,
    sourceContainerId: string,
    targetContainerId: string
  ) => void;
}

function QualityProfileItem({
  editGroups,
  containerId,
  index,
  groupId,
  qualityId,
  name,
  allowed,
  isFirst,
  isLast,
  moveTargets,
  onCreateGroupPress,
  onQualityProfileItemAllowedChange,
  onMoveItem,
  onMoveItemTo,
}: Readonly<QualityProfileItemProps>) {
  const itemId = qualityKey(qualityId);

  // A quality in the root list can land on anything; one inside a group only
  // swaps places with other qualities.
  const { ref, handleRef, isDragging } = useSortable({
    id: itemId,
    index,
    group: containerId,
    type: 'quality',
    accept: containerId === ROOT_CONTAINER ? undefined : ['quality'],
  });

  const moveOptions: SortableHandleOption[] = [
    {
      isDisabled: isFirst,
      label: translate('MoveItemUp', { item: name }),
      onPress: () => onMoveItem(itemId, containerId, -1),
    },
    {
      isDisabled: isLast,
      label: translate('MoveItemDown', { item: name }),
      onPress: () => onMoveItem(itemId, containerId, 1),
    },
    ...(containerId === ROOT_CONTAINER
      ? []
      : [
          {
            label: translate('MoveItemOutsideGroup', { item: name }),
            onPress: () => onMoveItemTo(itemId, containerId, ROOT_CONTAINER),
          },
        ]),
    ...moveTargets
      .filter((target) => target.id !== containerId)
      .map((target) => ({
        label: translate('MoveItemTo', {
          item: name,
          destination: target.name,
        }),
        onPress: () => onMoveItemTo(itemId, containerId, target.id),
      })),
  ];

  const handleAllowedChange = useCallback(
    ({ value }: CheckInputChanged) => {
      onQualityProfileItemAllowedChange?.(qualityId, value);
    },
    [qualityId, onQualityProfileItemAllowedChange]
  );

  const handleCreateGroupPress = useCallback(() => {
    onCreateGroupPress?.(qualityId);
  }, [qualityId, onCreateGroupPress]);

  return (
    <div
      ref={ref}
      className={classNames(
        styles.qualityProfileItem,
        isDragging && styles.isDragging,
        groupId && styles.isInGroup
      )}
    >
      <label className={styles.qualityNameContainer}>
        {editGroups && !groupId && (
          <IconButton
            className={styles.createGroupButton}
            name={icons.GROUP}
            title={translate('Group')}
            onPress={handleCreateGroupPress}
          />
        )}

        {!editGroups && (
          <CheckInput
            className={styles.checkInput}
            containerClassName={styles.checkInputContainer}
            name={name}
            value={allowed}
            isDisabled={!!groupId}
            onChange={handleAllowedChange}
          />
        )}

        <div
          className={classNames(
            styles.qualityName,
            groupId && styles.isInGroup,
            !allowed && styles.notAllowed
          )}
        >
          {name}
        </div>
      </label>

      <SortableHandle
        className={styles.dragHandle}
        handleRef={handleRef}
        label={name}
        options={moveOptions}
      />
    </div>
  );
}

export default QualityProfileItem;
