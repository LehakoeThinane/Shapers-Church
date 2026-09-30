/// <reference types="node" />
import { existsSync } from 'node:fs';
import type { ConfigContext, ExpoConfig } from 'expo/config';

/**
 * Extends app.json with settings that come from the environment:
 * - GOOGLE_SERVICES_JSON: the Firebase config for Android push (an EAS "file" environment variable on the
 *   build servers; locally, ./google-services.json if present). It is never committed.
 * - EAS_PROJECT_ID: the Expo project, needed for push tokens. Set by `eas init` or in the environment.
 */
export default ({ config }: ConfigContext): ExpoConfig => {
  const googleServicesFile = process.env.GOOGLE_SERVICES_JSON ?? (existsSync('./google-services.json') ? './google-services.json' : undefined);
  const projectId = process.env.EAS_PROJECT_ID ?? (config.extra?.eas as { projectId?: string } | undefined)?.projectId;

  return {
    ...(config as ExpoConfig),
    android: { ...config.android, ...(googleServicesFile ? { googleServicesFile } : {}) },
    extra: { ...config.extra, ...(projectId ? { eas: { projectId } } : {}) },
  };
};
