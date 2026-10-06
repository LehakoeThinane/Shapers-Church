import { useQuery } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from './api';

export type CellRole = Schemas['CellRole'];
export type DayOfWeek = NonNullable<Schemas['DayOfWeek']>;

export const days: DayOfWeek[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

export const roleLabel: Record<CellRole, string> = { Leader: 'Leader', CoLeader: 'Co-leader', Member: 'Member' };

export const nextStepLabel: Record<Schemas['NextStep'], string> = {
  Baptism: 'Baptism',
  GrowthTrack: 'Growth Track',
  Serving: 'Serving',
  Leadership: 'Leadership',
};

export const multiplicationLabel: Record<Schemas['MultiplicationReadiness'], string> = {
  NotYet: 'Not yet',
  Growing: 'Growing towards it',
  Ready: 'Ready to multiply',
};

/** The cells the signed-in person belongs to. Leaders get their "My cell" screens from this. */
export function useMyCells() {
  return useQuery({
    queryKey: ['me', 'cells'],
    queryFn: async () => unwrap(await api.GET('/api/me/cells')),
    staleTime: 60_000,
    retry: false,
  });
}

export const isLeader = (role: CellRole) => role === 'Leader' || role === 'CoLeader';

/** "Tuesdays at 19:00", from the API's day and "HH:mm:ss" time. */
export function meetingLabel(day: Schemas['DayOfWeek'] | undefined, time: string | null | undefined) {
  const when = time ? time.slice(0, 5) : null;
  if (day && when) return `${day}s at ${when}`;
  if (day) return `${day}s`;
  return when ? `At ${when}` : 'Meeting time not set';
}

export const today = () => new Date().toISOString().slice(0, 10);
