import AsyncStorage from '@react-native-async-storage/async-storage';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Constants, { ExecutionEnvironment } from 'expo-constants';
import * as Device from 'expo-device';
import type * as NotificationsModule from 'expo-notifications';
import { router } from 'expo-router';
import { useEffect } from 'react';
import { Platform } from 'react-native';
import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import { api, deviceName, unwrap, useSession, type Schemas } from './api';
import { currentNoticeVersion } from './privacy';

export type InboxItem = Schemas['NotificationDto'];
export type Topic = Schemas['Topic'];

const projectId = (Constants.expoConfig?.extra?.eas as { projectId?: string } | undefined)?.projectId;

/**
 * Push needs a real phone and our own app build: Expo Go on Android can't receive pushes, the browser preview
 * has none, and without an EAS project there is no token to register.
 */
export const pushSupported =
  Platform.OS !== 'web' &&
  Device.isDevice &&
  !!projectId &&
  !(Platform.OS === 'android' && Constants.executionEnvironment === ExecutionEnvironment.StoreClient);

interface PushState {
  /** The token registered for the signed-in member, so signing out can release it. */
  token: string | null;
  /** The member said "not now" to the prompt on Home. */
  dismissed: boolean;
  setToken: (token: string | null) => void;
  dismiss: () => void;
}

export const usePushStore = create<PushState>()(
  persist(
    (set) => ({
      token: null,
      dismissed: false,
      setToken: (token) => set({ token }),
      dismiss: () => set({ dismissed: true }),
    }),
    { name: 'shapers.push', storage: createJSONStorage(() => AsyncStorage) },
  ),
);

let loaded: typeof NotificationsModule | null = null;

/**
 * The notifications library, loaded only where push works. Merely importing it crashes Expo Go on Android
 * (its push support was removed there), so it is never loaded unless this is our own app build.
 */
function notifications(): typeof NotificationsModule | null {
  if (!pushSupported) return null;
  if (!loaded) {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    loaded = require('expo-notifications') as typeof NotificationsModule;
    loaded.setNotificationHandler({
      handleNotification: async () => ({ shouldShowBanner: true, shouldShowList: true, shouldPlaySound: false, shouldSetBadge: false }),
    });
  }
  return loaded;
}

async function registerThisPhone(): Promise<boolean> {
  const Notifications = notifications();
  if (!Notifications) return false;
  if (Platform.OS === 'android') {
    await Notifications.setNotificationChannelAsync('default', { name: 'Shapers Church', importance: Notifications.AndroidImportance.DEFAULT });
  }

  const { data: token } = await Notifications.getExpoPushTokenAsync({ projectId });
  unwrap(await api.POST('/api/me/devices', { body: { token, platform: Platform.OS, name: deviceName } }));
  usePushStore.getState().setToken(token);
  return true;
}

/** Asks the phone for permission, records the member's consent, and registers the phone. */
export async function enablePush(): Promise<boolean> {
  const Notifications = notifications();
  if (!Notifications) return false;
  const { status } = await Notifications.requestPermissionsAsync();
  if (status !== 'granted') return false;
  unwrap(
    await api.POST('/api/me/consents', {
      body: { decisions: [{ purpose: 'communications.push', granted: true }], policyVersion: await currentNoticeVersion(), source: 'MobileApp', lawfulBasis: 'Consent' },
    }),
  );
  return registerThisPhone();
}

/** Before signing out: this phone stops receiving the member's notifications. */
export async function releasePush(): Promise<void> {
  const token = usePushStore.getState().token;
  if (!token) return;
  await api.POST('/api/me/devices/unregister', { body: { token } }).catch(() => undefined);
  usePushStore.getState().setToken(null);
}

export function usePushPermission() {
  return useQuery({
    queryKey: ['push-permission'],
    enabled: pushSupported,
    queryFn: async () => (await notifications()!.getPermissionsAsync()).status,
  });
}

/**
 * Mounted once at the root: keeps the phone's registration fresh (tokens can change) and opens the right
 * screen when a notification is tapped, including one that launched the app.
 */
export function usePushLifecycle() {
  const status = useSession((s) => s.status);
  const queryClient = useQueryClient();

  useEffect(() => {
    const Notifications = notifications();
    if (!Notifications || status !== 'signedIn') return;
    void (async () => {
      if ((await Notifications.getPermissionsAsync()).status === 'granted') {
        await registerThisPhone().catch(() => undefined);
      }
    })();
  }, [status]);

  useEffect(() => {
    const Notifications = notifications();
    if (!Notifications) return;
    const open = (response: NotificationsModule.NotificationResponse | null) => {
      const link = response?.notification.request.content.data?.link;
      if (typeof link === 'string' && link.startsWith('/')) router.push(link as never);
    };
    void Notifications.getLastNotificationResponseAsync().then(open);
    const tapped = Notifications.addNotificationResponseReceivedListener(open);
    const received = Notifications.addNotificationReceivedListener(() => void queryClient.invalidateQueries({ queryKey: ['me', 'notifications'] }));
    return () => {
      tapped.remove();
      received.remove();
    };
  }, [queryClient]);
}

export function useInbox() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['me', 'notifications'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/notifications')),
    refetchInterval: 5 * 60_000,
  });
}

export function useMarkRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string | null) =>
      id
        ? unwrap(await api.POST('/api/me/notifications/{id}/read', { params: { path: { id } } }))
        : unwrap(await api.POST('/api/me/notifications/read-all')),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me', 'notifications'] }),
  });
}

export function usePreferences() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['me', 'notification-preferences'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/notification-preferences')),
  });
}

export function useSetPreference() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: Schemas['SetPreferenceRequest']) => unwrap(await api.PUT('/api/me/notification-preferences', { body })),
    onMutate: (body) =>
      queryClient.setQueryData<Schemas['PreferenceDto'][]>(['me', 'notification-preferences'], (list) =>
        list?.map((p) => (p.topic === body.topic && p.channel === body.channel ? { ...p, enabled: body.enabled } : p)),
      ),
    onSettled: () => void queryClient.invalidateQueries({ queryKey: ['me', 'notification-preferences'] }),
  });
}

export const topicLabels: Record<Topic, { title: string; hint: string }> = {
  Live: { title: 'Services going live', hint: 'When a service starts streaming' },
  Sermons: { title: 'New sermons', hint: 'When a sermon is published' },
  Events: { title: 'Events', hint: 'Your bookings, waiting lists and reminders' },
  Prayer: { title: 'Prayer', hint: 'Updates on your prayer requests' },
  Announcements: { title: 'Announcements', hint: 'News from the church' },
  Serving: { title: 'Serving', hint: 'When you’re asked to serve, and reminders before you do' },
};
