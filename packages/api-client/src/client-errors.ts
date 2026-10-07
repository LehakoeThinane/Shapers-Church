import type { components } from './schema.generated';

export type ClientErrorReport = components['schemas']['ClientErrorReport'];
export type ClientApp = 'admin' | 'member-app';

/**
 * Removes anything that could identify a person before a crash report leaves the device. The API scrubs again
 * (backend/src/Host/Shapers.Api/Hosting/ClientErrors.cs); keep the two lists in step.
 */
export function scrub(text: string | null | undefined, maxLength: number): string | null {
  if (!text?.trim()) return null;
  const clean = text
    .replace(/(https?:\/\/[^\s?#'"()]+)[?#][^\s'"():]*/g, '$1')
    .replace(/[\w.+-]+@[\w-]+(\.[\w-]+)+/g, '[email]')
    .replace(/\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi, '[id]')
    .replace(/\beyJ[\w-]+\.[\w-]+\.[\w-]+/g, '[token]')
    .replace(/\b[A-Za-z0-9_-]{32,}\b/g, '[token]')
    .replace(/\+?\d[\d ()-]{5,}\d/g, '[number]')
    .trim();
  return clean.length <= maxLength ? clean : `${clean.slice(0, maxLength)}…`;
}

/** A page's address with ids and numbers replaced, so it names the page and not the record. */
export function routePattern(path: string): string {
  return path
    .split(/[?#]/)[0]!
    .split('/')
    .map((s) => (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s) || /^\d+$/.test(s) ? ':id' : s))
    .join('/');
}

/**
 * Sends crash reports to the API, which logs them for the team. Fire and forget: reporting must never cause a
 * second error. Each distinct error is sent once per session, and at most a handful in all, so a crash loop
 * can't flood the API. No cookies or tokens go with it; the report says nothing about who was signed in.
 */
export function createErrorReporter(options: { baseUrl: string; app: ClientApp; version?: string; route: () => string }) {
  const seen = new Set<string>();
  const maxReports = 5;

  return function report(error: unknown) {
    try {
      const err = error instanceof Error ? error : new Error(typeof error === 'string' ? error : 'Unknown error');
      const body: ClientErrorReport = {
        app: options.app,
        version: options.version ?? null,
        route: scrub(routePattern(options.route()), 200),
        errorType: scrub(err.name, 100),
        message: scrub(err.message, 500),
        stack: scrub(err.stack?.split('\n').slice(0, 12).join('\n'), 2000),
      };
      const key = `${body.errorType}|${body.message}`;
      if (seen.has(key) || seen.size >= maxReports) return;
      seen.add(key);
      void fetch(`${options.baseUrl}/api/client-errors`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        credentials: 'omit',
        keepalive: true,
      }).catch(() => undefined);
    } catch {
      // Never let reporting a crash cause another one.
    }
  };
}
