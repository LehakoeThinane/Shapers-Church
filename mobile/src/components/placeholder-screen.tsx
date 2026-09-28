import { font, radius, space } from '@shapers/tokens';
import { GlassView } from 'expo-glass-effect';
import { ScrollView, StyleSheet, Text } from 'react-native';

import { useTheme } from '@/theme/theme';

/** Phase 0 stand-in for a tab: the real screens replace this one by one. */
export function PlaceholderScreen({ title, description }: { title: string; description: string }) {
  const { palette } = useTheme();
  return (
    <ScrollView
      style={{ backgroundColor: palette.color.background }}
      contentContainerStyle={styles.content}
      contentInsetAdjustmentBehavior="automatic">
      <Text style={[styles.title, { color: palette.color.text.primary }]}>{title}</Text>
      <GlassView
        colorScheme={palette.appearance}
        style={[styles.card, { backgroundColor: palette.color.glass.fill, borderColor: palette.color.glass.edge }]}>
        <Text style={[styles.body, { color: palette.color.text.secondary }]}>{description}</Text>
      </GlassView>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: space.lg, gap: space.lg },
  title: { fontSize: 30, fontWeight: '700', letterSpacing: -0.6 },
  card: { borderRadius: radius.card, padding: space.xl, borderWidth: StyleSheet.hairlineWidth },
  body: { fontSize: 15, lineHeight: 21, fontFamily: font.uiNative === 'Figtree' ? undefined : font.uiNative },
});
