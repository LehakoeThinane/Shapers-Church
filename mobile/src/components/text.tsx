import { SymbolView } from 'expo-symbols';
import type { ComponentProps } from 'react';
import { Text, TextInput, type TextInputProps, type TextProps, StyleSheet } from 'react-native';

import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

type Tone = 'primary' | 'secondary' | 'tertiary' | 'interactive' | 'accent' | 'danger';

/** Text in the current palette. Defaults to body style in the primary text colour. */
export function AppText({ tone = 'primary', style, ...rest }: TextProps & { tone?: Tone }) {
  const { palette } = useTheme();
  const c = palette.color;
  const color = {
    primary: c.text.primary,
    secondary: c.text.secondary,
    tertiary: c.text.tertiary,
    interactive: c.interactive,
    accent: c.accent,
    danger: c.danger,
  }[tone];
  return <Text {...rest} style={[text.body, { color }, style]} />;
}

type SymbolName = ComponentProps<typeof SymbolView>['name'];

/** SF Symbols on iOS, Material Symbols on Android. */
export function Icon({ name, size = 22, tone = 'interactive' }: { name: SymbolName; size?: number; tone?: 'interactive' | 'primary' | 'onAccent' }) {
  const { palette } = useTheme();
  const tint = { interactive: palette.color.interactive, primary: palette.color.text.primary, onAccent: palette.color.text.onAccent }[tone];
  return <SymbolView name={name} size={size} tintColor={tint} />;
}

export function Field(props: TextInputProps) {
  const { palette } = useTheme();
  return (
    <TextInput
      placeholderTextColor={palette.color.text.tertiary}
      {...props}
      style={[
        text.body,
        styles.field,
        { color: palette.color.text.primary, borderColor: palette.color.glass.edge, backgroundColor: palette.color.glass.fill },
        props.style,
      ]}
    />
  );
}

const styles = StyleSheet.create({
  field: { height: 50, borderRadius: 14, borderWidth: StyleSheet.hairlineWidth, paddingHorizontal: 16, fontSize: 17 },
});
