import React, { useCallback, useEffect, useState } from 'react';
import { setIsSidebarVisible, useAppValue } from 'App/appStore';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import useKeyboardShortcuts from 'Helpers/Hooks/useKeyboardShortcuts';
import useTheme from 'Helpers/Hooks/useTheme';
import { icons } from 'Helpers/Props';
import { imagesPath, isPinkTheme } from 'Styles/Themes';
import translate from 'Utilities/String/translate';
import KeyboardShortcutsModal from './KeyboardShortcutsModal';
import MovieSearchInput from './MovieSearchInput';
import PageHeaderActionsMenu from './PageHeaderActionsMenu';
import SafeForWorkButton from './SafeForWorkButton';
import styles from './PageHeader.module.css';

interface PageHeaderProps {
  isSmallScreen: boolean;
}

function PageHeader({ isSmallScreen }: Readonly<PageHeaderProps>) {
  const isSidebarVisible = useAppValue('isSidebarVisible');
  const theme = useTheme();
  // V2 has no wide wordmark, so its square logo stands in on every screen size.
  const showSquareLogo = isSmallScreen || isPinkTheme(theme);

  const [isKeyboardShortcutsModalOpen, setIsKeyboardShortcutsModalOpen] =
    useState(false);

  const { bindShortcut, unbindShortcut } = useKeyboardShortcuts();

  const handleSidebarToggle = useCallback(() => {
    setIsSidebarVisible({ isSidebarVisible: !isSidebarVisible });
  }, [isSidebarVisible]);

  const handleOpenKeyboardShortcutsModal = useCallback(() => {
    setIsKeyboardShortcutsModalOpen(true);
  }, []);

  const handleKeyboardShortcutsModalClose = useCallback(() => {
    setIsKeyboardShortcutsModalOpen(false);
  }, []);

  useEffect(() => {
    bindShortcut(
      'openKeyboardShortcutsModal',
      handleOpenKeyboardShortcutsModal
    );

    return () => {
      unbindShortcut('openKeyboardShortcutsModal');
    };
  }, [handleOpenKeyboardShortcutsModal, bindShortcut, unbindShortcut]);

  return (
    <div className={styles.header}>
      <div className={styles.logoContainer}>
        <Link className={styles.logoLink} to="/">
          <img
            className={showSquareLogo ? styles.logo : styles.logoFull}
            src={`${imagesPath(theme)}/${showSquareLogo ? 'logo.png' : 'logo-full.png'}`}
            alt="Whisparr Logo"
          />
        </Link>
      </div>

      <div className={styles.sidebarToggleContainer}>
        <IconButton
          id="sidebar-toggle-button"
          name={icons.NAVBAR_COLLAPSE}
          aria-label={translate('Menu')}
          onPress={handleSidebarToggle}
        />
      </div>

      <MovieSearchInput />

      <div className={styles.right}>
        <SafeForWorkButton />

        <IconButton
          className={styles.donate}
          name={icons.HEART}
          aria-label={translate('Donate')}
          to="https://whisparr.com/donate"
          size={14}
          title={translate('Donate')}
        />

        {/*
        <IconButton
          className={styles.translate}
          title={translate('SuggestTranslationChange')}
          name={icons.TRANSLATE}
          to="https://translate.servarr.com/projects/whisparr/whisparr/"
          size={24}
        />
        */}

        <PageHeaderActionsMenu
          onKeyboardShortcutsPress={handleOpenKeyboardShortcutsModal}
        />
      </div>

      <KeyboardShortcutsModal
        isOpen={isKeyboardShortcutsModalOpen}
        onModalClose={handleKeyboardShortcutsModalClose}
      />
    </div>
  );
}

export default PageHeader;
