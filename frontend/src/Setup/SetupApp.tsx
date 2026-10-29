import React, {
  useCallback,
  useEffect,
  useLayoutEffect,
  useState,
} from 'react';
import Button from 'Components/Link/Button';
import themes from 'Styles/Themes';
import translate, { setTranslations } from 'Utilities/String/translate';
import SetupCarriesOver from './SetupCarriesOver';
import SetupEnvTargets from './SetupEnvTargets';
import SetupProgress from './SetupProgress';
import SetupSourceForm from './SetupSourceForm';
import SetupSummary from './SetupSummary';
import useSetupStatus, {
  LegacyKind,
  postSetup,
  SetupChoice,
  SetupInstall,
  SetupStatus,
} from './useSetupStatus';
import styles from './SetupApp.module.css';

const REFUSAL_KEYS: Record<string, string> = {
  v2TooOld: 'SetupRefusedV2TooOld',
  erosNewerThanV5: 'SetupRefusedErosNewer',
};

// v5 has no database in setup mode, so the localization API is not available;
// the page reads the English strings that ship with the app instead.
function useSetupTranslations() {
  const [isReady, setIsReady] = useState(false);

  useEffect(() => {
    import('../../../src/NzbDrone.Core/Localization/Core/en.json')
      .then((module) => setTranslations(module.default))
      .catch(() => undefined)
      .finally(() => setIsReady(true));
  }, []);

  return isReady;
}

// The normal app applies its theme from the UI settings, which need the
// database; here the theme the server handed the page is all there is.
function useSetupTheme() {
  useLayoutEffect(() => {
    const { theme } = window.Whisparr;
    const resolved =
      theme === 'light' || theme === 'dark'
        ? theme
        : (window.matchMedia('(prefers-color-scheme: dark)').matches &&
            'dark') ||
          'light';

    Object.entries(themes[resolved]).forEach(([key, value]) => {
      document.documentElement.style.setProperty(`--${key}`, value);
    });
  }, []);
}

function defaultsFor(status: SetupStatus, kind: LegacyKind): SetupChoice {
  if (kind === status.defaults.kind) {
    return {
      kind,
      sceneSource: status.defaults.sceneSource,
      movieSource: status.defaults.movieSource,
    };
  }

  const install = status.installs.find((i) => i.kind === kind);

  if (kind === 'v2') {
    return { kind, sceneSource: 'tpdb', movieSource: 'tpdb' };
  }

  return {
    kind,
    sceneSource: 'stashDb',
    movieSource:
      !install || install.previousMovieSource === 'none'
        ? 'tmdb'
        : install.previousMovieSource,
  };
}

// Polls initialize.json until the app is back out of setup mode, then reloads
// into it.
function useReloadWhenBack(isRestarting: boolean) {
  useEffect(() => {
    if (!isRestarting) {
      return;
    }

    const timer = window.setInterval(async () => {
      try {
        const response = await fetch(
          `${window.Whisparr.urlBase}/initialize.json?t=${Date.now()}`
        );
        const json = (await response.json()) as { setupMode?: boolean };

        if (response.ok && json.setupMode === false) {
          window.location.reload();
        }
      } catch {
        // Still down; try again.
      }
    }, 2000);

    return () => window.clearInterval(timer);
  }, [isRestarting]);
}

function needsEnvSwitch(status: SetupStatus) {
  return (
    status.pendingEnvSwitch ||
    (status.progress.status === 'succeeded' &&
      status.progress.envVarsToChange.length > 0)
  );
}

function EnvSwitchScreen({
  status,
  onShutDown,
}: Readonly<{ status: SetupStatus; onShutDown: () => void }>) {
  const targets =
    status.progress.envTargets.length > 0
      ? status.progress.envTargets
      : status.envTargets;

  return (
    <>
      <h1>{translate('SetupEnvSwitchReady')}</h1>

      <SetupEnvTargets targets={targets} />

      <p>{translate('SetupEnvSwitchInstruction')}</p>

      <Button onPress={onShutDown}>{translate('SetupShutDown')}</Button>
    </>
  );
}

function SetupApp() {
  useSetupTheme();

  const isTranslated = useSetupTranslations();
  const { status, hasError, refresh } = useSetupStatus();
  const [choice, setChoice] = useState<SetupChoice | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const progressStatus = status?.progress.status;
  const isRestarting =
    progressStatus === 'restarting' ||
    (hasError &&
      (progressStatus === 'succeeded' || progressStatus === 'running'));

  useReloadWhenBack(isRestarting);

  const activeChoice =
    choice ?? (status ? defaultsFor(status, status.defaults.kind) : null);

  const handleKindChange = useCallback(
    (kind: LegacyKind) => {
      if (status) {
        setChoice(defaultsFor(status, kind));
      }
    },
    [status]
  );

  const handleMigrate = useCallback(async () => {
    if (!activeChoice) {
      return;
    }

    setIsBusy(true);
    setSubmitError(null);

    try {
      await postSetup('migrate', activeChoice);
      await refresh();
    } catch (error) {
      setSubmitError(error instanceof Error ? error.message : String(error));
    } finally {
      setIsBusy(false);
    }
  }, [activeChoice, refresh]);

  const handleRetry = useCallback(async () => {
    await postSetup('retry').catch(() => undefined);
    await refresh();
  }, [refresh]);

  const handleShutDown = useCallback(async () => {
    await postSetup('shutdown').catch(() => undefined);
  }, []);

  if (!isTranslated || !status || !activeChoice) {
    return (
      <div className={styles.page}>
        {hasError ? <p>{translate('SetupStatusError')}</p> : null}
      </div>
    );
  }

  const progress = status.progress;

  if (needsEnvSwitch(status)) {
    return (
      <div className={styles.page}>
        <EnvSwitchScreen status={status} onShutDown={handleShutDown} />
      </div>
    );
  }

  const install: SetupInstall | undefined = status.installs.find(
    (i) => i.kind === activeChoice.kind
  );
  const isIdle = progress.status === 'idle';
  const refusal = install ? REFUSAL_KEYS[install.refusal] : undefined;
  const hasErrors = status.errors.length > 0 || !install;

  let body: React.ReactNode = null;

  if (hasErrors) {
    body = (
      <div className={styles.errors}>
        <h3>{translate('SetupDetectionErrors')}</h3>

        <ul>
          {status.errors.map((error) => (
            <li key={error}>{error}</li>
          ))}
        </ul>

        <Button onPress={handleShutDown}>{translate('SetupShutDown')}</Button>
      </div>
    );
  } else if (!isIdle) {
    body = (
      <SetupProgress
        progress={progress}
        onRetry={handleRetry}
        onShutDown={handleShutDown}
      />
    );
  } else if (refusal) {
    body = (
      <div className={styles.errors}>
        <p>{translate(refusal)}</p>

        <Button onPress={handleShutDown}>{translate('SetupShutDown')}</Button>
      </div>
    );
  } else if (install) {
    body = (
      <SetupSourceForm
        install={install}
        choice={activeChoice}
        envTargets={status.envTargets}
        isBusy={isBusy}
        submitError={submitError ?? progress.errorMessage}
        onChange={setChoice}
        onMigrate={handleMigrate}
        onShutDown={handleShutDown}
      />
    );
  }

  return (
    <div className={styles.page}>
      <h1>{translate('SetupHeader')}</h1>

      {install && !hasErrors ? (
        <SetupSummary
          installs={status.installs}
          kind={activeChoice.kind}
          onKindChange={handleKindChange}
        />
      ) : null}

      {body}

      {install && !hasErrors && isIdle && !refusal ? (
        <SetupCarriesOver
          kind={activeChoice.kind}
          sameSource={
            activeChoice.sceneSource === install.previousSceneSource &&
            activeChoice.movieSource === install.previousMovieSource
          }
        />
      ) : null}
    </div>
  );
}

export default SetupApp;
