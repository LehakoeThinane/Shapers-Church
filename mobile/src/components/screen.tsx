import { space } from '@shapers/tokens';
import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';

import { GlowBackground } from './glow-background';

/** A tab screen: glowing background, scrolling content, and room for the floating tab bar. */
export function Screen({ children }: { children: ReactNode }) {
  return (
    <View style={styles.root}>
      <GlowBackground />
      <ScrollView contentInsetAdjustmentBehavior="automatic" contentContainerStyle={styles.content}>
        {children}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, overflow: 'hidden' },
  content: { paddingHorizontal: space.lg, paddingTop: space.xs, paddingBottom: 120, gap: space.xl },
});
