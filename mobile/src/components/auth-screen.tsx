import { space } from '@shapers/tokens';
import type { ReactNode } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';

import { Glass } from './glass';
import { GlowBackground } from './glow-background';
import { AppText } from './text';
import { text } from '@/theme/type';

export function AuthScreen({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <View style={styles.root}>
      <GlowBackground />
      <KeyboardAvoidingView style={styles.root} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <AppText style={text.largeTitle}>{title}</AppText>
          <AppText tone="secondary">{subtitle}</AppText>
          <Glass style={styles.card}>{children}</Glass>
        </ScrollView>
      </KeyboardAvoidingView>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, overflow: 'hidden' },
  content: { padding: space.lg, paddingTop: 110, gap: space.md },
  card: { padding: space.lg, gap: space.md, marginTop: space.sm },
});
