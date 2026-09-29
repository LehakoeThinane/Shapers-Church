import { palettes } from './tokens.generated';

export { palettes, font, radius, space, blur } from './tokens.generated';

export type PaletteName = keyof typeof palettes;

/** What a member chose. "auto" follows the phone or browser's light/dark setting. */
export type PalettePreference = PaletteName | 'auto';

export const paletteNames = Object.keys(palettes) as PaletteName[];

type Widen<T> = T extends string
  ? string
  : T extends number
    ? number
    : T extends readonly (infer U)[]
      ? readonly Widen<U>[]
      : { readonly [K in keyof T]: Widen<T[K]> };

/** The shape every palette shares. Components depend on this, never on a specific palette. */
export type Palette = Omit<Widen<(typeof palettes)['midnight']>, 'appearance'> & {
  readonly appearance: 'light' | 'dark';
};

export function getPalette(name: PaletteName): Palette {
  return palettes[name];
}

/** Midnight is the dark look, Rose the light look. */
export function resolvePalette(preference: PalettePreference, systemScheme: 'light' | 'dark' | null | undefined): PaletteName {
  if (preference !== 'auto') return preference;
  return systemScheme === 'light' ? 'rose' : 'midnight';
}

export function isPalettePreference(value: unknown): value is PalettePreference {
  return value === 'auto' || value === 'midnight' || value === 'rose';
}
