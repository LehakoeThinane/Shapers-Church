import { Figtree_400Regular, Figtree_500Medium, Figtree_600SemiBold, Figtree_700Bold } from '@expo-google-fonts/figtree';
import { Newsreader_400Regular_Italic } from '@expo-google-fonts/newsreader';
import { isPalettePreference } from '@shapers/tokens';
import { focusManager, QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { useFonts } from 'expo-font';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useState } from 'react';
import { AppState } from 'react-native';

import { api, ApiError, restoreSession, unwrap, useSession } from '@/lib/api';
import { ThemeProvider, usePaletteStore, useTheme } from '@/theme/theme';

void SplashScreen.preventAutoHideAsync();

// Pause polling (e.g. the Live tab) while the app is in the background, to save data and battery.
focusManager.setEventListener((setFocused) => {
  const subscription = AppState.addEventListener('change', (state) => setFocused(state === 'active'));
  return () => subscription.remove();
});

export default function RootLayout() {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            retry: (failures, error) => !(error instanceof ApiError && error.status < 500) && failures < 2,
            staleTime: 60_000,
          },
        },
      }),
  );
  const [fontsLoaded] = useFonts({
    Figtree_400Regular,
    Figtree_500Medium,
    Figtree_600SemiBold,
    Figtree_700Bold,
    Newsreader_400Regular_Italic,
  });
  const status = useSession((s) => s.status);

  useEffect(() => {
    void restoreSession();
  }, []);

  const ready = fontsLoaded && status !== 'unknown';
  useEffect(() => {
    if (ready) void SplashScreen.hideAsync();
  }, [ready]);

  if (!ready) return null;

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <AccountPalette />
        <Navigator />
      </ThemeProvider>
    </QueryClientProvider>
  );
}

function Navigator() {
  const { palette } = useTheme();
  return (
    <>
      <StatusBar style={palette.appearance === 'dark' ? 'light' : 'dark'} />
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: palette.color.background } }}>
        <Stack.Screen name="(tabs)" />
        <Stack.Screen name="sermon/[slug]" options={{ headerShown: true, headerTransparent: true, headerTitle: '', headerBackButtonDisplayMode: 'minimal', headerTintColor: palette.color.interactive }} />
        <Stack.Screen name="series/[slug]" options={{ headerShown: true, headerTransparent: true, headerTitle: '', headerBackButtonDisplayMode: 'minimal', headerTintColor: palette.color.interactive }} />
        <Stack.Screen name="profile" options={{ presentation: 'modal' }} />
        <Stack.Screen name="(auth)" options={{ presentation: 'modal' }} />
      </Stack>
    </>
  );
}

/** After sign-in on a new phone, adopt the palette saved on the member's account. */
function AccountPalette() {
  const status = useSession((s) => s.status);
  const setPreference = usePaletteStore((s) => s.setPreference);
  const access = useQuery({
    queryKey: ['me', 'access'],
    queryFn: async () => unwrap(await api.GET('/api/me/access')),
    enabled: status === 'signedIn',
  });

  useEffect(() => {
    const saved = access.data?.palette;
    if (isPalettePreference(saved) && saved !== 'auto') setPreference(saved);
  }, [access.data?.palette, setPreference]);

  return null;
}
