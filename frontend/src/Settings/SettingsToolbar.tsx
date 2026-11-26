import React, { ReactElement, useEffect } from 'react';
import PendingChangesModal from 'Components/Modal/PendingChangesModal';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton, {
  PageToolbarButtonProps,
} from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import useKeyboardShortcuts from 'Helpers/Hooks/useKeyboardShortcuts';
import useUnsavedChanges from 'Helpers/Hooks/useUnsavedChanges';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import AdvancedSettingsButton from './AdvancedSettingsButton';

interface SettingsToolbarProps {
  showSave?: boolean;
  isSaving?: boolean;
  hasPendingChanges?: boolean;
  // TODO: This should do type checking like PageToolbarSectionProps,
  // but this works for the time being.
  additionalButtons?: ReactElement | null;
  onSavePress?: () => void;
}

function SettingsToolbar({
  showSave = true,
  isSaving,
  hasPendingChanges,
  additionalButtons = null,
  onSavePress,
}: Readonly<SettingsToolbarProps>) {
  const { bindShortcut, unbindShortcut } = useKeyboardShortcuts();
  const { pendingChangesModalProps } = useUnsavedChanges({
    hasUnsavedChanges: !!hasPendingChanges,
  });

  useEffect(() => {
    bindShortcut(
      'saveSettings',
      (e) => {
        // Keeps the browser's own Save Page dialog from opening as well
        e.preventDefault();

        if (hasPendingChanges) {
          onSavePress?.();
        }
      },
      {
        isGlobal: true,
      }
    );

    return () => {
      unbindShortcut('saveSettings');
    };
  }, [hasPendingChanges, bindShortcut, unbindShortcut, onSavePress]);

  return (
    <PageToolbar>
      <PageToolbarSection>
        <AdvancedSettingsButton showLabel={true} />
        {showSave ? (
          <PageToolbarButton
            label={
              hasPendingChanges
                ? translate('SaveChanges')
                : translate('NoChanges')
            }
            iconName={icons.SAVE}
            isSpinning={isSaving}
            isDisabled={!hasPendingChanges}
            onPress={onSavePress}
          />
        ) : null}

        {additionalButtons as ReactElement<PageToolbarButtonProps> | null}
      </PageToolbarSection>

      <PendingChangesModal {...pendingChangesModalProps} />
    </PageToolbar>
  );
}

export default SettingsToolbar;
