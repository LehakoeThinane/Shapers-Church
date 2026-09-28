import createClient, { type Client } from 'openapi-fetch';
import type { components, paths } from './schema.generated';

export type { components, paths };
export type Schemas = components['schemas'];
export type ShapersClient = Client<paths>;

/** RFC 7807 problem response, with the API's stable error code. */
export interface ApiProblem {
  status?: number;
  title?: string;
  code?: string;
  traceId?: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ApiProblem;

  constructor(status: number, problem: ApiProblem) {
    super(problem.title ?? `Request failed (${status})`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get code(): string | undefined {
    return this.problem.code;
  }
}

/** Unwraps an openapi-fetch result, throwing an {@link ApiError} for problem responses. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.error !== undefined || !result.response.ok) {
    throw new ApiError(result.response.status, (result.error ?? {}) as ApiProblem);
  }
  return result.data as T;
}

export const CSRF_HEADER = 'X-Shapers-CSRF';

/**
 * Admin portal: the browser holds an HttpOnly session cookie, so no token ever touches JavaScript.
 * Every request carries the CSRF header the API requires for cookie-authenticated writes.
 */
export function createCookieClient(options: { baseUrl?: string; onUnauthorized?: () => void } = {}): ShapersClient {
  const client = createClient<paths>({ baseUrl: options.baseUrl ?? '', credentials: 'include' });
  client.use({
    onRequest({ request }) {
      request.headers.set(CSRF_HEADER, '1');
      return request;
    },
    onResponse({ response, request }) {
      if (response.status === 401 && !new URL(request.url).pathname.startsWith('/api/auth/')) {
        options.onUnauthorized?.();
      }
      return response;
    },
  });
  return client;
}

export interface TokenStore {
  getAccessToken(): string | null | Promise<string | null>;
  getRefreshToken(): string | null | Promise<string | null>;
  save(tokens: { accessToken: string; refreshToken: string }): void | Promise<void>;
  clear(): void | Promise<void>;
}

/**
 * Member app: bearer tokens. On a 401 the client refreshes once (shared by concurrent requests),
 * retries, and signs the member out if the refresh is refused.
 */
export function createTokenClient(options: {
  baseUrl: string;
  tokens: TokenStore;
  device?: string;
  onSignedOut?: () => void;
}): ShapersClient {
  let refreshing: Promise<boolean> | null = null;

  const refresh = (): Promise<boolean> =>
    (refreshing ??= (async () => {
      try {
        const refreshToken = await options.tokens.getRefreshToken();
        if (!refreshToken) return false;
        const response = await fetch(`${options.baseUrl}/api/auth/refresh`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken, device: options.device ?? null }),
        });
        if (!response.ok) {
          await options.tokens.clear();
          options.onSignedOut?.();
          return false;
        }
        const pair = (await response.json()) as Schemas['TokenPair'];
        await options.tokens.save({ accessToken: pair.accessToken, refreshToken: pair.refreshToken });
        return true;
      } finally {
        refreshing = null;
      }
    })());

  const authorisedFetch = async (request: Request): Promise<Response> => {
    const send = async (req: Request) => {
      const token = await options.tokens.getAccessToken();
      if (token) req.headers.set('Authorization', `Bearer ${token}`);
      return fetch(req);
    };

    const isAuthCall = new URL(request.url).pathname.startsWith('/api/auth/');
    const response = await send(request.clone());
    if (response.status !== 401 || isAuthCall) return response;
    return (await refresh()) ? send(request) : response;
  };

  return createClient<paths>({ baseUrl: options.baseUrl, fetch: authorisedFetch });
}
