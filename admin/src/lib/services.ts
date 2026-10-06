import { useQuery } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from './api';

export type PlanItem = Schemas['PlanItem'];
export type PlanItemKind = Schemas['PlanItemKind'];
export type AssignmentStatus = Schemas['AssignmentStatus'];

/** "09:00:00" → "09:00". */
export const hhmm = (time: string | null | undefined) => (time ? time.slice(0, 5) : '');

/** Seconds as "6 min" or "1 h 10". */
export function duration(seconds: number) {
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  return `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, '0')}`;
}

/** "Sunday 11 October". */
export const longDay = (date: string) =>
  new Intl.DateTimeFormat('en-ZA', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date(`${date}T12:00:00`));

export const shortDay = (date: string) => new Intl.DateTimeFormat('en-ZA', { weekday: 'short', day: 'numeric', month: 'short' }).format(new Date(`${date}T12:00:00`));

export const statusLabel: Record<AssignmentStatus, string> = { Pending: 'Asked', Accepted: 'Yes', Declined: "Can't" };

export const statusTone: Record<AssignmentStatus, 'neutral' | 'success' | 'danger'> = { Pending: 'neutral', Accepted: 'success', Declined: 'danger' };

export const emptyItem = (kind: PlanItemKind): PlanItem => ({
  id: '00000000-0000-0000-0000-000000000000',
  kind,
  title: kind === 'Header' ? 'Section' : '',
  lengthSeconds: kind === 'Header' ? 0 : 300,
  description: null,
  songId: null,
  arrangementId: null,
  key: null,
  leader: null,
});

export function useTeams() {
  return useQuery({ queryKey: ['services', 'teams'], queryFn: async () => unwrap(await api.GET('/api/admin/services/teams')) });
}

export function useSongs(q = '') {
  return useQuery({
    queryKey: ['services', 'songs', q],
    queryFn: async () => unwrap(await api.GET('/api/admin/services/songs', { params: { query: { q: q || undefined } } })),
  });
}

/** Every position across the teams, labelled "Team: Position". */
export function positionsOf(teams: Schemas['TeamDto'][] | undefined) {
  return (teams ?? []).flatMap((t) => t.positions.map((p) => ({ id: p.id, teamId: t.id, label: `${t.name}: ${p.name}` })));
}
