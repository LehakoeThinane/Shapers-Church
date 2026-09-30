import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { api, unwrap, useSession, type Schemas } from './api';

export type WallItem = Schemas['PrayerWallItemDto'];
export type MyPrayer = Schemas['MyPrayerRequestDto'];

export function usePrayerWall() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['prayer', 'wall'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/prayer/wall')),
  });
}

export function useMyPrayers() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['prayer', 'mine'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/prayer-requests')),
  });
}

function useRefreshPrayer() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['prayer'] });
}

export function useSubmitPrayer() {
  const refresh = useRefreshPrayer();
  return useMutation({
    mutationFn: async (body: Schemas['SubmitPrayerRequest']) => unwrap(await api.POST('/api/prayer/requests', { body })),
    onSuccess: refresh,
  });
}

/** "I prayed": updates the wall straight away, then confirms with the server. */
export function usePrayed() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/prayer/requests/{id}/prayed', { params: { path: { id } } })),
    onMutate: (id) => {
      queryClient.setQueryData<WallItem[]>(['prayer', 'wall'], (items) =>
        items?.map((w) => (w.id === id && !w.iPrayed ? { ...w, iPrayed: true, prayedCount: w.prayedCount + 1 } : w)),
      );
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: ['prayer', 'wall'] }),
  });
}

export function useMarkAnswered() {
  const refresh = useRefreshPrayer();
  return useMutation({
    mutationFn: async ({ id, note }: { id: string; note: string | null }) =>
      unwrap(await api.POST('/api/me/prayer-requests/{id}/answered', { params: { path: { id } }, body: { note } })),
    onSuccess: refresh,
  });
}

export function useWithdrawPrayer() {
  const refresh = useRefreshPrayer();
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/me/prayer-requests/{id}/withdraw', { params: { path: { id } } })),
    onSuccess: refresh,
  });
}

export function prayerStatusLabel(p: MyPrayer): string {
  switch (p.status) {
    case 'AwaitingReview':
      return 'Waiting to go on the wall';
    case 'OnWall':
      return 'On the prayer wall';
    case 'WithPastors':
      return p.visibility === 'Wall' ? 'Kept with the pastors' : 'With the pastors';
    default:
      return 'Closed';
  }
}
