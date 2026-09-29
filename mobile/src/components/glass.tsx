import { radius } from '@shapers/tokens';
import { BlurView } from 'expo-blur';
import { GlassView, isLiquidGlassAvailable } from 'expo-glass-effect';
import type { ReactNode } from 'react';
import { Platform, Pressable, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native';
import Svg, { Defs, LinearGradient, Rect, Stop } from 'react-native-svg';

import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';
import { AppText } from './text';

const liquidGlass = Platform.OS === 'ios' && isLiquidGlassAvailable();

/**
 * A glass surface. iOS 26: real Liquid Glass. Older iOS: system blur. Android: a translucent fill
 * (real-time blur on Android costs too much battery on the phones many members use).
 */
export function Glass({
  children,
  style,
  interactive = false,
  cornerRadius = radius.xl,
}: {
  children?: ReactNode;
  style?: StyleProp<ViewStyle>;
  interactive?: boolean;
  cornerRadius?: number;
}) {
  const { palette } = useTheme();
  const { glass, glow } = palette.color;
  const edge: ViewStyle = {
    borderRadius: cornerRadius,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: glass.edge,
    shadowColor: glow.edge,
    shadowOpacity: palette.appearance === 'dark' ? 1 : 0,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 0 },
  };

  if (liquidGlass) {
    return (
      <GlassView glassEffectStyle="regular" isInteractive={interactive} colorScheme={palette.appearance} style={[edge, style]}>
        {children}
      </GlassView>
    );
  }

  if (Platform.OS === 'ios') {
    return (
      <View style={[edge, style]}>
        <BlurView
          intensity={40}
          tint={palette.appearance === 'dark' ? 'dark' : 'light'}
          style={[StyleSheet.absoluteFill, { borderRadius: cornerRadius, overflow: 'hidden', backgroundColor: glass.fill }]}
        />
        {children}
      </View>
    );
  }

  return <View style={[edge, { backgroundColor: glass.fill, elevation: 0 }, style]}>{children}</View>;
}

/** The primary action: gold gradient in Midnight, deep taupe in Rose. */
export function PrimaryButton({ label, onPress, disabled, busy }: { label: string; onPress: () => void; disabled?: boolean; busy?: boolean }) {
  const { palette } = useTheme();
  const { button, text: ink } = palette.color;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: disabled || busy, busy }}
      disabled={disabled || busy}
      onPress={onPress}
      style={({ pressed }) => [styles.button, { opacity: disabled ? 0.5 : pressed ? 0.85 : 1 }]}>
      <Svg style={StyleSheet.absoluteFill}>
        <Defs>
          <LinearGradient id="primary" x1="0" y1="0" x2="0" y2="1">
            <Stop offset="0" stopColor={button.primaryTop} />
            <Stop offset="1" stopColor={button.primaryBottom} />
          </LinearGradient>
        </Defs>
        <Rect width="100%" height="100%" fill="url(#primary)" />
      </Svg>
      <AppText style={[text.headline, { color: ink.onButton }]}>{busy ? 'Please wait…' : label}</AppText>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    height: 48,
    borderRadius: 24,
    overflow: 'hidden',
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 20,
  },
});
