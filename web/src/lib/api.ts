import type { Schemas } from '@shapers/api-client';

export type { Schemas };

/** Where the build reads content from. The browser uses PUBLIC_API_URL for live data and bookings. */
const API_URL = (import.meta.env.API_URL ?? 'http://localhost:5080').replace(/\/$/, '');
export const PUBLIC_API_URL = (import.meta.env.PUBLIC_API_URL ?? 'http://localhost:5080').replace(/\/$/, '');

/**
 * CI builds the site without an API to check it compiles; ALLOW_OFFLINE_BUILD=1 lets pages fall back to empty
 * content there. Real builds must reach the API, so a missing API fails the deploy instead of publishing blanks.
 */
const offlineAllowed = import.meta.env.ALLOW_OFFLINE_BUILD === '1';

export async function get<T>(path: string, fallback: T): Promise<T> {
  try {
    const response = await fetch(`${API_URL}${path}`, { headers: { Accept: 'application/json' } });
    if (response.status === 404) return fallback;
    if (!response.ok) throw new Error(`${path} returned ${response.status}`);
    return (await response.json()) as T;
  } catch (error) {
    if (offlineAllowed) return fallback;
    throw new Error(`Couldn't read ${path} from ${API_URL}. Is the API running? (${String(error)})`);
  }
}

/** Every page of a paged list, for building one page per item. */
export async function getAll<T>(path: string, pageSize = 50): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; page < 100; page++) {
    const separator = path.includes('?') ? '&' : '?';
    const result = await get<{ items: T[]; total: number }>(`${path}${separator}page=${page}&pageSize=${pageSize}`, { items: [], total: 0 });
    items.push(...result.items);
    if (result.items.length < pageSize || items.length >= result.total) break;
  }
  return items;
}
