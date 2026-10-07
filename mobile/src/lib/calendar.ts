import { useQuery } from '@tanstack/react-query';

import { api, API_URL, unwrap, useSession, type Schemas } from './api';

export type CalendarEntry = Schemas['CalendarEntry'];

const ZONE = 'Africa/Johannesburg';
const WEEKS = 8;

/** The next eight weeks: public events and livestreams, and when signed in, members' events and your own cell and serving dates. */
export function useCalendar() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['calendar', status],
    enabled: status !== 'unknown',
    queryFn: async () => {
      const from = new Date();
      from.setMinutes(0, 0, 0);
      const to = new Date(from.getTime() + WEEKS * 7 * 86_400_000);
      return unwrap(await api.GET('/api/calendar', { params: { query: { from: from.toISOString(), to: to.toISOString() } } }));
    },
  });
}

/** "2026-10-07" for an instant, as the church's calendar date. */
export const dayKey = (iso: string) =>
  new Intl.DateTimeFormat('en-CA', { timeZone: ZONE, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(iso));

export const timeOf = (iso: string) => new Intl.DateTimeFormat('en-ZA', { timeZone: ZONE, hour: '2-digit', minute: '2-digit' }).format(new Date(iso));

export const dayHeading = (key: string) =>
  new Intl.DateTimeFormat('en-ZA', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).format(new Date(`${key}T00:00:00Z`));

/** The public feed, as a subscription link the phone's calendar app understands. */
export const calendarFeedUrl = `${API_URL.replace(/^https?:/, 'webcal:')}/calendar.ics`;
