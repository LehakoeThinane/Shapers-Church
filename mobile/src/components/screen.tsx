import { space } from '@shapers/tokens';
import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';

import { Glass } from './glass';
import { GlowBackground } from './glow-background';
import { MiniPlayer } from './mini-player';
import { hasTabAccessory, usePlayerStore } from '@/lib/player';

/** A tab screen: glowing background, scrolling content, and room for the floating tab bar. */
export function Screen({ children }: { children: ReactNode }) {
  const playing = usePlayerStore((s) => s.current !== null);
  const floatingPlayer = playing && !hasTabAccessory;
  return (
    <View style={styles.root}>
      <GlowBackground />
      <ScrollView contentInsetAdjustmentBehavior="automatic" contentContainerStyle={styles.content}>
        {children}
      </ScrollView>
      {floatingPlayer && (
        <Glass style={styles.player}>
          <MiniPlayer />
        </Glass>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, overflow: 'hidden' },
  content: { paddingHorizontal: space.lg, paddingTop: space.xs, paddingBottom: 180, gap: space.xl },
  player: { position: 'absolute', left: space.md, right: space.md, bottom: space.md },
});
