import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';
import { Platform } from 'react-native';

import { api, ApiError, unwrap, useSession, type Schemas } from './api';

export type PrivacyRequest = Schemas['MyDataRequestDto'];

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
