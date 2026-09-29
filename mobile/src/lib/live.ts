import { useQuery } from '@tanstack/react-query';

import { api, unwrap, type Schemas } from './api';

export type LiveNow = Schemas['LiveNowDto'];
export type PublicStream = Schemas['PublicLivestreamDto'];

/** The church's giving page, used for the Give button until in-app giving arrives. */
export const FALLBACK_GIVE_URL = 'https://shaperschurch.com/giving/';

/**
 * What's on now. Polls quickly only while a service is live (the server caches for 10 s), slowly when
 * one is coming up, and rarely otherwise, so an idle app costs almost no data.
 */
export function useLiveNow() {
  return useQuery({
    queryKey: ['live-now'],
    queryFn: async () => unwrap(await api.GET('/api/media/live')),
    refetchInterval: (query) => {
      const state = query.state.data?.state;
      return state === 'Live' ? 10_000 : state === 'Upcoming' ? 60_000 : 5 * 60_000;
    },
    staleTime: 5_000,
  });
}

export function startsIn(iso: string, now = Date.now()): string {
  const minutes = Math.round((new Date(iso).getTime() - now) / 60_000);
  if (minutes <= 0) return 'Starting soon';
  if (minutes < 60) return `Starts in ${minutes} min`;
  const when = new Date(iso);
  const sameDay = new Date(now).toDateString() === when.toDateString();
  const time = new Intl.DateTimeFormat('en-ZA', { hour: '2-digit', minute: '2-digit' }).format(when);
  return sameDay ? `Today at ${time}` : `${new Intl.DateTimeFormat('en-ZA', { weekday: 'long' }).format(when)} at ${time}`;
}
