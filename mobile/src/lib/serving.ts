import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { api, unwrap, useSession, type Schemas } from './api';

export type MyAssignment = Schemas['MyAssignmentDto'];
export type Blockout = Schemas['BlockoutDto'];

export function useMySchedule() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['serving', 'mine'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/serving')),
  });
}

export function useBlockouts() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['serving', 'blockouts'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/serving/blockouts')),
  });
}

function useRefresh() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['serving'] });
}

export function useAnswer() {
  const refresh = useRefresh();
  return useMutation({
    mutationFn: async ({ id, accept, reason }: { id: string; accept: boolean; reason?: string }) =>
      unwrap(await api.POST('/api/me/serving/{id}/answer', { params: { path: { id } }, body: { accept, reason: reason || null } })),
    onSuccess: refresh,
  });
}

export function useAddBlockout() {
  const refresh = useRefresh();
  return useMutation({
    mutationFn: async (body: Schemas['SaveBlockoutRequest']) => unwrap(await api.POST('/api/me/serving/blockouts', { body })),
    onSuccess: refresh,
  });
}

export function useDeleteBlockout() {
  const refresh = useRefresh();
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.DELETE('/api/me/serving/blockouts/{id}', { params: { path: { id } } })),
    onSuccess: refresh,
  });
}

/** The plan as the band and team see it: order of service, songs with charts and lyrics, and who's serving. */
export function useRehearse(planId: string) {
  return useQuery({
    queryKey: ['serving', 'rehearse', planId],
    queryFn: async () => unwrap(await api.GET('/api/services/plans/{id}/rehearse', { params: { path: { id: planId } } })),
  });
}

/** "Sunday 11 October". */
export const servingDay = (date: string) =>
  new Intl.DateTimeFormat('en-ZA', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date(`${date}T12:00:00`));

export const hhmm = (time: string) => time.slice(0, 5);
