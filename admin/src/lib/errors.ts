import { createErrorReporter } from '@shapers/api-client';
import { ApiError, apiBaseUrl } from './api';

/** Crashes go to the API's log (and from there to Application Insights), stripped of personal details. */
const report = createErrorReporter({
  baseUrl: apiBaseUrl,
  app: 'admin',
  version: import.meta.env.VITE_APP_VERSION,
  route: () => window.location.pathname,
});

/**
 * Reports a crash. Refused requests are not crashes: the API already logs its own failures, and the page
 * shows them to the person.
 */
export function reportError(error: unknown) {
  if (error instanceof ApiError) return;
  report(error);
}

/** Catches what React never sees: errors in event handlers, timers and promises nobody awaited. */
export function reportUncaughtErrors() {
  window.addEventListener('error', (event) => {
    // The browser's own resize notice, not a fault in the portal.
    if (event.message?.startsWith('ResizeObserver loop')) return;
    reportError(event.error ?? event.message);
  });
  window.addEventListener('unhandledrejection', (event) => reportError(event.reason));
}
