import { useQuery } from '@tanstack/react-query';
import { api, unwrap } from './api';

export function useMembershipStatuses() {
  return useQuery({
    queryKey: ['membership-statuses'],
    queryFn: async () => unwrap(await api.GET('/api/admin/membership-statuses')),
    staleTime: 5 * 60_000,
  });
}

export function useCampuses() {
  return useQuery({
    queryKey: ['campuses'],
    queryFn: async () => unwrap(await api.GET('/api/admin/campuses')),
    staleTime: 5 * 60_000,
  });
}

export function useScopes() {
  return useQuery({
    queryKey: ['scopes'],
    queryFn: async () => unwrap(await api.GET('/api/admin/scopes')),
    staleTime: 5 * 60_000,
  });
}
