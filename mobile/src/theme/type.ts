import { Platform, type TextStyle } from 'react-native';

/**
 * iOS uses the system font (SF Pro), as in the mockup. Android uses Figtree, loaded in the root layout.
 * Newsreader italic is the serif accent for scripture and series titles on every platform.
 */
const figtree = {
  '400': 'Figtree_400Regular',
  '500': 'Figtree_500Medium',
  '600': 'Figtree_600SemiBold',
  '700': 'Figtree_700Bold',
} as const;

export function ui(weight: '400' | '500' | '600' | '700' = '400'): TextStyle {
  return Platform.OS === 'ios' ? { fontWeight: weight } : { fontFamily: figtree[weight] };
}

export const serif: TextStyle = { fontFamily: 'Newsreader_400Regular_Italic' };

export const text = {
  largeTitle: { ...ui('700'), fontSize: 30, letterSpacing: -0.6 },
  title: { ...ui('700'), fontSize: 19, letterSpacing: -0.2 },
  headline: { ...ui('600'), fontSize: 17 },
  body: { ...ui('400'), fontSize: 15, lineHeight: 21 },
  callout: { ...ui('500'), fontSize: 13 },
  caption: { ...ui('500'), fontSize: 12.5 },
  label: { ...ui('600'), fontSize: 11, letterSpacing: 0.9, textTransform: 'uppercase' },
} satisfies Record<string, TextStyle>;
