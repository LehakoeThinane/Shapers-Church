import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useEffect, useRef } from 'react';
import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';
import { Platform } from 'react-native';

import { api, ApiError, unwrap, useSession, type Schemas } from './api';

export type PrivacyRequest = Schemas['MyDataRequestDto'];

/** Used only if the notice can't be fetched (e.g. offline); consent records then show the version the app shipped with. */
const FALLBACK_NOTICE_VERSION = '2026-09';
let cachedVersion: string | null = null;

/** The privacy notice version people are agreeing to right now, from the server. */
export async function currentNoticeVersion(): Promise<string> {
  if (cachedVersion) return cachedVersion;
  try {
    cachedVersion = unwrap(await api.GET('/api/privacy/notice')).version;
    return cachedVersion;
  } catch {
    return FALLBACK_NOTICE_VERSION;
  }
}

export function usePrivacyNotice() {
  return useQuery({
    queryKey: ['privacy-notice'],
    queryFn: async () => unwrap(await api.GET('/api/privacy/notice')),
    staleTime: 60 * 60_000,
  });
}

export function usePrivacyStatus() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['me', 'privacy-status'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/privacy-status')),
    staleTime: 60 * 60_000,
  });
}

/** Records that the member has read the current notice (and agrees to the church keeping their record). */
export function useAcceptNotice() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (version: string) =>
      unwrap(
        await api.POST('/api/me/consents', {
          body: { decisions: [{ purpose: 'processing.church_record', granted: true }], policyVersion: version, source: 'MobileApp', lawfulBasis: 'Consent' },
        }),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me', 'privacy-status'] }),
  });
}

/** Mounted at the root: once per session, a member who hasn't accepted the current notice is asked to read it. */
export function usePrivacyReview() {
  const { data } = usePrivacyStatus();
  const asked = useRef(false);
  useEffect(() => {
    if (data?.needsReview && !asked.current) {
      asked.current = true;
      router.push({ pathname: '/privacy-notice', params: { review: '1' } });
    }
  }, [data?.needsReview]);
}

/** Downloads everything the church holds about the member and opens the share sheet so they can save it. */
export async function downloadMyData(): Promise<void> {
  const result = await api.GET('/api/me/data-export', { parseAs: 'text' });
  const data: unknown = result.data;
  if (!result.response.ok || typeof data !== 'string') {
    throw new ApiError(result.response.status, { title: 'We couldn’t prepare your data. Please try again.' });
  }

  if (Platform.OS === 'web') {
    const url = URL.createObjectURL(new Blob([data], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'shapers-church-my-data.json';
    link.click();
    URL.revokeObjectURL(url);
    return;
  }

  const file = new File(Paths.cache, 'shapers-church-my-data.json');
  if (file.exists) file.delete();
  file.create();
  file.write(data);
  await Sharing.shareAsync(file.uri, { mimeType: 'application/json', dialogTitle: 'Your Shapers Church data' });
}

export function useMyPrivacyRequests() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['me', 'privacy-requests'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/privacy-requests')),
  });
}

export function useSubmitPrivacyRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: Schemas['SubmitDataRequest']) => unwrap(await api.POST('/api/me/privacy-requests', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me', 'privacy-requests'] }),
  });
}

export function requestLabel(r: PrivacyRequest): string {
  const what = r.type === 'Deletion' ? 'Delete my account and data' : 'Correct my details';
  const state = r.status === 'Open' ? 'with our Information Officer' : r.status === 'Completed' ? 'done' : 'not done';
  return `${what}: ${state}`;
}
