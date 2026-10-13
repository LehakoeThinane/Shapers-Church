import { useMutation, useQuery } from '@tanstack/react-query';

import { api, unwrap, useSession, type Schemas } from './api';

export type GivingPage = Schemas['GivingPageDto'];
export type MyGift = Schemas['MyGiftDto'];

/** Funds, whether card giving is on, and the bank details. Public: visitors see it too. */
export function useGivingPage() {
  return useQuery({ queryKey: ['giving', 'page'], queryFn: async () => unwrap(await api.GET('/api/giving')), staleTime: 5 * 60_000 });
}

export function useMyGiving() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['giving', 'mine'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/giving')),
  });
}

/** Starts a card gift and returns the payment page's address. */
export function useStartGift() {
  return useMutation({
    mutationFn: async (body: Schemas['StartGiftRequest']) => unwrap(await api.POST('/api/giving/checkout', { body })),
  });
}

/** "R 1 250,00". */
export const rands = (cents: number) => new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(cents / 100);

/** "250", "250.50" or "250,50" as a number of rands; null if it isn't one. */
export function parseRands(text: string): number | null {
  const cleaned = text.trim().replace(/\s/g, '').replace(',', '.');
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null;
  const value = Number(cleaned);
  return value >= 5 && value <= 500_000 ? value : null;
}
