import type { Schemas } from '@shapers/api-client';

export const apiUrl = (import.meta.env.PUBLIC_API_URL ?? 'http://localhost:5080').replace(/\/$/, '');

export async function liveNow(): Promise<Schemas['LiveNowDto'] | null> {
  try {
    const response = await fetch(`${apiUrl}/api/media/live`);
    return response.ok ? ((await response.json()) as Schemas['LiveNowDto']) : null;
  } catch {
    return null;
  }
}

/** POST JSON to the API and return the parsed body, or throw with the API's own message. */
export async function post<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    let message = 'Something went wrong. Please try again.';
    try {
      const problem = (await response.json()) as { title?: string };
      if (problem.title) message = problem.title;
    } catch {
      // Not JSON: keep the general message.
    }
    throw new Error(message);
  }
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`, { headers: { Accept: 'application/json' } });
  if (!response.ok) throw new Error(response.status === 404 ? 'Not found.' : 'Something went wrong. Please try again.');
  return (await response.json()) as T;
}
