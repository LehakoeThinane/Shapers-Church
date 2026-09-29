import { useEffect } from 'react';
import { StyleSheet } from 'react-native';
import Animated, { Easing, useAnimatedStyle, useReducedMotion, useSharedValue, withRepeat, withTiming } from 'react-native-reanimated';
import Svg, { Defs, Ellipse, RadialGradient, Stop } from 'react-native-svg';

import { useTheme } from '@/theme/theme';

/** Soft coloured light behind the glass, as in the mockup. Drifts slowly unless Reduce Motion is on. */
export function GlowBackground() {
  const { palette } = useTheme();
  const reduceMotion = useReducedMotion();
  const drift = useSharedValue(0);

  useEffect(() => {
    if (reduceMotion) return;
    drift.value = withRepeat(withTiming(1, { duration: 18_000, easing: Easing.inOut(Easing.sin) }), -1, true);
  }, [drift, reduceMotion]);

  const style = useAnimatedStyle(() => ({
    transform: [{ translateX: drift.value * 16 }, { translateY: drift.value * -12 }, { scale: 1 + drift.value * 0.05 }],
  }));

  const { glow } = palette.color;
  const blobs = [
    { id: 'a', color: glow.primary, cx: '85%', cy: '12%', rx: '48%', ry: '30%' },
    { id: 'b', color: glow.secondary, cx: '5%', cy: '45%', rx: '55%', ry: '35%' },
    { id: 'c', color: glow.accent, cx: '80%', cy: '70%', rx: '40%', ry: '22%' },
    { id: 'd', color: glow.tertiary, cx: '20%', cy: '95%', rx: '50%', ry: '30%' },
  ];

  return (
    <Animated.View pointerEvents="none" style={[styles.fill, { backgroundColor: palette.color.background }, style]}>
      <Svg width="100%" height="100%">
        <Defs>
          {blobs.map((b) => (
            <RadialGradient key={b.id} id={b.id} cx="50%" cy="50%" r="50%">
              <Stop offset="0" stopColor={b.color} stopOpacity={1} />
              <Stop offset="0.7" stopColor={b.color} stopOpacity={0} />
            </RadialGradient>
          ))}
        </Defs>
        {blobs.map((b) => (
          <Ellipse key={b.id} cx={b.cx} cy={b.cy} rx={b.rx} ry={b.ry} fill={`url(#${b.id})`} />
        ))}
      </Svg>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  fill: { position: 'absolute', top: '-10%', bottom: '-10%', left: '-10%', right: '-10%' },
});
