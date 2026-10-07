import { createErrorReporter } from '@shapers/api-client';
import Constants from 'expo-constants';
import { useSegments } from 'expo-router';
import { useEffect } from 'react';

import { API_URL, ApiError } from '@/lib/api';

/** The current screen as its route pattern (for example "sermon/[slug]"), never the values in it. */
let currentRoute = '/';

const report = createErrorReporter({
  baseUrl: API_URL,
  app: 'member-app',
  version: Constants.expoConfig?.version,
  route: () => currentRoute,
});

/**
 * Reports a crash, stripped of personal details, to the API's log. Refused requests are not crashes: the API
 * already logs its own failures, and the screen shows them to the member.
 */
export function reportError(error: unknown) {
  if (error instanceof ApiError) return;
  report(error);
}

/** Keeps track of the screen, so a report says where the crash happened. */
export function useTrackRoute() {
  const segments = useSegments();
  useEffect(() => {
    currentRoute = `/${segments.join('/')}`;
  }, [segments]);
}

type GlobalHandler = (error: unknown, isFatal?: boolean) => void;
const errorUtils = (globalThis as { ErrorUtils?: { getGlobalHandler(): GlobalHandler; setGlobalHandler(handler: GlobalHandler): void } }).ErrorUtils;

/** Catches errors React never sees (event handlers, timers), then lets React Native handle them as before. */
export function reportUncaughtErrors() {
  if (!errorUtils) return;
  const previous = errorUtils.getGlobalHandler();
  errorUtils.setGlobalHandler((error, isFatal) => {
    reportError(error);
    previous(error, isFatal);
  });
}
