import { ApiError, createTokenClient, unwrap, type Schemas, type TokenStore } from '@shapers/api-client';
import * as Device from 'expo-device';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import { create } from 'zustand';

export { ApiError, unwrap };
export type { Schemas };

/**
 * Where the API lives. Set EXPO_PUBLIC_API_URL for a phone on the same Wi-Fi (http://<your-PC-IP>:5080).
 * Defaults suit the iOS simulator and the Android emulator (which reaches the host as 10.0.2.2).
 */
export const API_URL =
  process.env.EXPO_PUBLIC_API_URL ?? (Platform.OS === 'android' ? 'http://10.0.2.2:5080' : 'http://localhost:5080');

const ACCESS_KEY = 'shapers.accessToken';
const REFRESH_KEY = 'shapers.refreshToken';

type SessionStatus = 'unknown' | 'signedOut' | 'signedIn';

interface SessionState {
  status: SessionStatus;
  setStatus: (status: SessionStatus) => void;
}

/** Whether a member is signed in. Tokens themselves live only in the device keychain / keystore. */
export const useSession = create<SessionState>((set) => ({
  status: 'unknown',
  setStatus: (status) => set({ status }),
}));

const tokens: TokenStore = {
  getAccessToken: () => SecureStore.getItemAsync(ACCESS_KEY),
  getRefreshToken: () => SecureStore.getItemAsync(REFRESH_KEY),
  save: async ({ accessToken, refreshToken }) => {
    await SecureStore.setItemAsync(ACCESS_KEY, accessToken);
    await SecureStore.setItemAsync(REFRESH_KEY, refreshToken);
  },
  clear: async () => {
    await SecureStore.deleteItemAsync(ACCESS_KEY);
    await SecureStore.deleteItemAsync(REFRESH_KEY);
  },
};

export const deviceName = Device.modelName ?? Platform.OS;

export const api = createTokenClient({
  baseUrl: API_URL,
  tokens,
  device: deviceName,
  onSignedOut: () => useSession.getState().setStatus('signedOut'),
});

export async function startSession(pair: Schemas['TokenPair']) {
  await tokens.save({ accessToken: pair.accessToken, refreshToken: pair.refreshToken });
  useSession.getState().setStatus('signedIn');
}

export async function restoreSession() {
  const refresh = await tokens.getRefreshToken();
  useSession.getState().setStatus(refresh ? 'signedIn' : 'signedOut');
}

export async function signOut() {
  const refreshToken = await tokens.getRefreshToken();
  if (refreshToken) {
    await api.POST('/api/auth/logout', { body: { refreshToken } }).catch(() => undefined);
  }

  await tokens.clear();
  useSession.getState().setStatus('signedOut');
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError || error instanceof Error) return error.message;
  return 'Something went wrong. Please try again.';
}
