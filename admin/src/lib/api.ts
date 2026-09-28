import { ApiError, createCookieClient, unwrap, type Schemas } from '@shapers/api-client';

export { ApiError, unwrap };
export type { Schemas };

/** Session-cookie client. A 401 anywhere outside sign-in means the session ended: go to the sign-in page. */
export const api = createCookieClient({
  onUnauthorized: () => {
    const { pathname, search } = window.location;
    if (!pathname.startsWith('/login') && !pathname.startsWith('/set-password')) {
      window.location.assign(`/login?next=${encodeURIComponent(pathname + search)}`);
    }
  },
});

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message;
  return 'Something went wrong.';
}

export const formatDate = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-ZA', { dateStyle: 'medium' }).format(new Date(value)) : '—';

export const formatDateTime = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat('en-ZA', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—';
