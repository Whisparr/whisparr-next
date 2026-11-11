import { useCallback, useEffect } from 'react';
import useTheme from 'Helpers/Hooks/useTheme';
import themes, { imagesPath, isPinkTheme } from 'Styles/Themes';

function ApplyTheme() {
  // `useTheme` resolves `auto` against the system theme and follows it when it
  // changes; `themes.auto` is fixed at page load, so reading it here would not.
  const theme = useTheme();

  const updateCSSVariables = useCallback(() => {
    Object.entries(themes[theme]).forEach(([key, value]) => {
      document.documentElement.style.setProperty(`--${key}`, value);
    });
  }, [theme]);

  // On Component Mount and Component Update
  useEffect(() => {
    updateCSSVariables();
  }, [updateCSSVariables, theme]);

  // Under a pink theme, point the favicons at V2's copies of the same files;
  // otherwise restore what index.html set, which keeps the server's debug icon.
  useEffect(() => {
    document
      .querySelectorAll<HTMLLinkElement>('link[rel~="icon"]')
      .forEach((link) => {
        link.dataset.defaultHref ??= link.href;
        const fileName = new URL(link.dataset.defaultHref).pathname
          .split('/')
          .pop();

        link.href = isPinkTheme(theme)
          ? `${imagesPath(theme)}/Icons/${fileName}`
          : link.dataset.defaultHref;
      });
  }, [theme]);

  return null;
}

export default ApplyTheme;
