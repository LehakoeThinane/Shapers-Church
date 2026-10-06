import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from './api';

export type Draft = Schemas['DraftDto'];

/** Whether AI help is on and this person may use it. Screens hide their AI buttons otherwise. */
export function useAssistStatus() {
  return useQuery({
    queryKey: ['assist', 'status'],
    queryFn: async () => unwrap(await api.GET('/api/admin/assist/status')),
    staleTime: 60_000,
    retry: false,
  });
}

export function useCanDraft() {
  const { data } = useAssistStatus();
  return !!data?.canDraft && !data.budgetReached;
}

/** Records whether a person used a draft or threw it away. */
export function useReviewDraft() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, action }: { id: string; action: 'accept' | 'discard' }) =>
      action === 'accept'
        ? unwrap(await api.POST('/api/admin/assist/drafts/{id}/accept', { params: { path: { id } } }))
        : unwrap(await api.POST('/api/admin/assist/drafts/{id}/discard', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['assist'] }),
  });
}
