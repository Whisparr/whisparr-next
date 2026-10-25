import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App/App';

import 'Diag/ConsoleApi';

export async function bootstrap() {
  document.title = window.Whisparr.instanceName;

  const container = document.getElementById('root');

  const root = createRoot(container!);

  // No database exists yet, so the normal app can't run: show the migration page.
  if (window.Whisparr.setupMode) {
    const { default: SetupApp } = await import('./Setup/SetupApp');

    root.render(<SetupApp />);

    return;
  }

  root.render(<App />);
}
