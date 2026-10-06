import { getPalette, resolvePalette } from '@shapers/tokens';
import { useEffect } from 'react';
import { Pressable, Text, useColorScheme, View } from 'react-native';

import { reportError } from '@/lib/errors';
import { usePaletteStore } from '@/theme/theme';

/**
 * Shown when a screen crashes. It may be outside the theme provider and before fonts load, so it reads the
 * palette itself and uses the system font.
 */
export function CrashScreen({ error, retry }: { error: Error; retry: () => Promise<void> }) {
  const system = useColorScheme();
  const preference = usePaletteStore((s) => s.preference);
  const { color } = getPalette(resolvePalette(preference, system === 'light' || system === 'dark' ? system : null));

  useEffect(() => reportError(error), [error]);

  return (
    <View style={{ flex: 1, justifyContent: 'center', padding: 24, gap: 12, backgroundColor: color.background }}>
      <Text accessibilityRole="header" style={{ fontSize: 24, fontWeight: '700', color: color.text.primary }}>
        Something went wrong
      </Text>
      <Text style={{ fontSize: 16, lineHeight: 22, color: color.text.secondary }}>
        We&apos;ve let the team know. Try again, and if it keeps happening, close the app and open it again.
      </Text>
      <Pressable
        accessibilityRole="button"
        onPress={() => void retry()}
        style={{ alignSelf: 'flex-start', marginTop: 8, paddingVertical: 12, paddingHorizontal: 20, borderRadius: 999, backgroundColor: color.button.primaryBottom }}>
        <Text style={{ fontSize: 16, fontWeight: '600', color: color.text.onButton }}>Try again</Text>
      </Pressable>
    </View>
  );
}
