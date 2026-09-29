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

/** Whether a member is signed in. On phones the tokens live only in the device keychain / keystore. */
export const useSession = create<SessionState>((set) => ({
  status: 'unknown',
  setStatus: (status) => set({ status }),
}));

/**
 * Browsers have no keychain, and the web build is for development previews only. Keep tokens for this tab
 * alone (sessionStorage), so closing the tab signs out; fall back to memory if storage is blocked.
 */
const webStore = (() => {
  const memory = new Map<string, string>();
  const storage = () => {
    try {
      return globalThis.sessionStorage ?? null;
    } catch {
      return null;
    }
  };
  return {
    getItemAsync: async (key: string) => storage()?.getItem(key) ?? memory.get(key) ?? null,
    setItemAsync: async (key: string, value: string) => {
      try {
        storage()?.setItem(key, value);
      } catch {
        // Storage full or blocked: memory still holds it for this page.
      }
      memory.set(key, value);
    },
    deleteItemAsync: async (key: string) => {
      try {
        storage()?.removeItem(key);
      } catch {
        // Nothing to remove.
      }
      memory.delete(key);
    },
  };
})();

const store = Platform.OS === 'web' ? webStore : SecureStore;

const tokens: TokenStore = {
  getAccessToken: () => store.getItemAsync(ACCESS_KEY),
  getRefreshToken: () => store.getItemAsync(REFRESH_KEY),
  save: async ({ accessToken, refreshToken }) => {
    await store.setItemAsync(ACCESS_KEY, accessToken);
    await store.setItemAsync(REFRESH_KEY, refreshToken);
  },
  clear: async () => {
    await store.deleteItemAsync(ACCESS_KEY);
    await store.deleteItemAsync(REFRESH_KEY);
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
