import { useCallback, useEffect, useRef, useState } from 'react';

export type LegacyKind = 'eros' | 'v2';
export type SceneSource = 'stashDb' | 'tpdb';
export type MovieSource = 'none' | 'tmdb' | 'tpdb';
export type SetupState =
  'idle' | 'running' | 'failed' | 'succeeded' | 'restarting';

export interface SetupInstall {
  kind: LegacyKind;
  databaseType: string;
  refusal: string;
  studios: number;
  scenes: number;
  movies: number;
  files: number;
  previousSceneSource: SceneSource;
  previousMovieSource: MovieSource;
  hasMixedMovies: boolean;
  namedByEnvironment: boolean;
}

export interface SetupEnvTarget {
  name: string;
  value: string;
}

export interface SetupStepStatus {
  step: string;
  state: string;
}

export interface SetupProgressStatus {
  status: SetupState;
  steps: SetupStepStatus[];
  backupPath: string | null;
  errorMessage: string | null;
  errorKey: string | null;
  canRetry: boolean;
  envVarsToChange: string[];
  envTargets: SetupEnvTarget[];
  skippedMovies: number;
  skippedMoviePaths: string[];
}

export interface SetupStatus {
  installs: SetupInstall[];
  errors: string[];
  pendingEnvSwitch: boolean;
  envTargets: SetupEnvTarget[];
  defaults: {
    kind: LegacyKind;
    sceneSource: SceneSource;
    movieSource: MovieSource;
  };
  progress: SetupProgressStatus;
}

export interface SetupChoice {
  kind: LegacyKind;
  sceneSource: SceneSource;
  movieSource: MovieSource;
}

function setupUrl(path: string) {
  return `${window.Whisparr.urlBase}/api/setup/${path}`;
}

export async function postSetup(path: string, body?: unknown) {
  const response = await fetch(setupUrl(path), {
    method: 'POST',
    headers: {
      'X-Api-Key': window.Whisparr.apiKey,
      'Content-Type': 'application/json',
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    let message = `${response.status}`;

    try {
      const json = (await response.json()) as { message?: string };

      message = json.message ?? message;
    } catch {
      // The body wasn't JSON; the status code is all there is.
    }

    throw new Error(message);
  }
}

// Polls once a second while a migration is running and the server is answering,
// and never while it is idle: nothing changes until the user acts.
export default function useSetupStatus() {
  const [status, setStatus] = useState<SetupStatus | null>(null);
  const [hasError, setHasError] = useState(false);
  const timer = useRef<number | undefined>(undefined);
  const latest = useRef<SetupStatus | null>(null);

  const refresh = useCallback(async () => {
    window.clearTimeout(timer.current);

    try {
      const response = await fetch(setupUrl('status'), {
        headers: { 'X-Api-Key': window.Whisparr.apiKey },
      });

      // The setup API only exists in setup mode, so a 404 means the app has
      // restarted into its normal mode.
      if (response.status === 404) {
        window.location.reload();

        return;
      }

      if (!response.ok) {
        throw new Error(`${response.status}`);
      }

      const next = (await response.json()) as SetupStatus;

      latest.current = next;
      setStatus(next);
      setHasError(false);
    } catch {
      setHasError(true);
    }

    const current = latest.current;

    // The env-switch screen only waits for the user to change the variables
    // and restart, so nothing on the server changes while it is open.
    const isEnvSwitch =
      current !== null &&
      (current.pendingEnvSwitch || current.progress.envVarsToChange.length > 0);
    const busy =
      current !== null &&
      !isEnvSwitch &&
      (current.progress.status === 'running' ||
        current.progress.status === 'succeeded' ||
        current.progress.status === 'restarting');

    // A server that stopped answering is restarting or shutting down; keep asking.
    if (busy || current === null) {
      timer.current = window.setTimeout(refresh, 1000);
    }
  }, []);

  useEffect(() => {
    refresh().catch(() => undefined);

    return () => window.clearTimeout(timer.current);
  }, [refresh]);

  return { status, hasError, refresh };
}
