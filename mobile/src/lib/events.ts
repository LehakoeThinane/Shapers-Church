import AsyncStorage from '@react-native-async-storage/async-storage';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import { api, unwrap, useSession, type Schemas } from './api';

export type ChurchEvent = Schemas['EventDto'];
export type Registration = Schemas['RegistrationDto'];

/** What the door scanner reads. The prefix stops other QR codes being mistaken for tickets. */
export const ticketQrValue = (code: string) => `SHAPERS-T:${code}`;

export function useUpcomingEvents() {
  const status = useSession((s) => s.status);
  return useQuery({
    // Signed-in members also see members-only events.
    queryKey: ['events', status === 'signedIn'],
    queryFn: async () => unwrap(await api.GET('/api/events')),
    staleTime: 5 * 60_000,
  });
}

export function useEvent(slug: string | undefined) {
  return useQuery({
    queryKey: ['event', slug],
    enabled: !!slug,
    queryFn: async () => unwrap(await api.GET('/api/events/{slug}', { params: { path: { slug: slug! } } })),
  });
}

export function useHousehold() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['me', 'household'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/household')),
  });
}

interface TicketCache {
  registrations: Registration[];
  save: (registrations: Registration[]) => void;
}

/** The last tickets we saw, kept on the phone so they open at the door without signal. */
export const useTicketCache = create<TicketCache>()(
  persist((set) => ({ registrations: [], save: (registrations) => set({ registrations }) }), {
    name: 'shapers.tickets',
    storage: createJSONStorage(() => AsyncStorage),
  }),
);

/** My registrations: live from the server when online, from the phone when not. */
export function useMyRegistrations() {
  const status = useSession((s) => s.status);
  const cached = useTicketCache((s) => s.registrations);
  const save = useTicketCache((s) => s.save);
  const query = useQuery({
    queryKey: ['me', 'registrations'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/registrations')),
  });

  useEffect(() => {
    if (query.data) save(query.data);
  }, [query.data, save]);
  useEffect(() => {
    if (status === 'signedOut') save([]);
  }, [status, save]);

  const all = query.data ?? cached;
  const upcoming = all.filter((r) => r.status !== 'Cancelled' && new Date(r.startsAt).getTime() > Date.now() - 12 * 3_600_000);
  return { ...query, upcoming, offline: !query.data && cached.length > 0 };
}

export function useRegister(slug: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: Schemas['MemberRegisterRequest']) =>
      unwrap(await api.POST('/api/events/{slug}/register', { params: { path: { slug } }, body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['me', 'registrations'] });
      void queryClient.invalidateQueries({ queryKey: ['event', slug] });
      void queryClient.invalidateQueries({ queryKey: ['events'] });
    },
  });
}

export function useCancelRegistration() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/me/registrations/{id}/cancel', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['me', 'registrations'] });
      void queryClient.invalidateQueries({ queryKey: ['events'] });
      void queryClient.invalidateQueries({ queryKey: ['event'] });
    },
  });
}

const dayFormat = new Intl.DateTimeFormat('en-ZA', { weekday: 'short', day: 'numeric', month: 'short' });
const timeFormat = new Intl.DateTimeFormat('en-ZA', { hour: '2-digit', minute: '2-digit' });

export const eventDay = (iso: string) => dayFormat.format(new Date(iso));
export const eventTime = (iso: string) => timeFormat.format(new Date(iso));
export const eventWhen = (iso: string) => `${eventDay(iso)} · ${eventTime(iso)}`;

export function seatsNote(e: ChurchEvent): string | null {
  if (!e.registrationRequired) return null;
  if (!e.registrationOpen) return e.registrationClosedReason ?? 'Registration is closed';
  if (e.waitlistOnly) return 'Full: join the waiting list';
  if (e.seatsLeft !== null && e.seatsLeft <= 20) return `${e.seatsLeft} seat${e.seatsLeft === 1 ? '' : 's'} left`;
  return 'Registration open';
}
