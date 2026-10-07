import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { api, unwrap, useSession, type Schemas } from './api';

export type MyChild = Schemas['MyChildDto'];

export function useMyKids() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['kids', 'mine'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/kids')),
    // The pickup code changes to "collected" while the parent waits at the door.
    refetchInterval: 60_000,
  });
}

function useSetKids() {
  const queryClient = useQueryClient();
  return (kids: MyChild[]) => queryClient.setQueryData(['kids', 'mine'], kids);
}

export function useAddChild() {
  const set = useSetKids();
  return useMutation({
    mutationFn: async (body: Schemas['AddChildRequest']) => unwrap(await api.POST('/api/me/kids', { body })),
    onSuccess: set,
  });
}

export function useUpdateCareNotes() {
  const set = useSetKids();
  return useMutation({
    mutationFn: async ({ childId, ...body }: Schemas['CareNotesRequest'] & { childId: string }) =>
      unwrap(await api.PUT('/api/me/kids/{childId}/care-notes', { params: { path: { childId } }, body })),
    onSuccess: set,
  });
}

export function useCheckInKids() {
  const set = useSetKids();
  return useMutation({
    mutationFn: async (childIds: string[]) => unwrap(await api.POST('/api/me/kids/check-in', { body: { childIds } })),
    onSuccess: set,
  });
}

export const timeOf = (iso: string) => new Intl.DateTimeFormat('en-ZA', { timeZone: 'Africa/Johannesburg', hour: '2-digit', minute: '2-digit' }).format(new Date(iso));

/** "2019-03-14" from what a parent types: 14/03/2019, 2019-03-14 or 14 3 2019. Null if it isn't a real date. */
export function parseDate(text: string): string | null {
  const parts = text.trim().split(/[\s/.-]+/).map(Number);
  if (parts.length !== 3 || parts.some((n) => !Number.isInteger(n))) return null;
  const [a, b, c] = parts as [number, number, number];
  const [year, month, day] = a > 31 ? [a, b, c] : [c, b, a];
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return null;
  return date.toISOString().slice(0, 10);
}
