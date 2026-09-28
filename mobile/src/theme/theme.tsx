import { getPalette, resolvePalette, type Palette, type PaletteName, type PalettePreference } from '@shapers/tokens';
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Appearance, useColorScheme } from 'react-native';

interface ThemeValue {
  name: PaletteName;
  palette: Palette;
  preference: PalettePreference;
  setPreference: (preference: PalettePreference) => void;
}

const ThemeContext = createContext<ThemeValue | null>(null);

/**
 * Midnight or Rose, chosen by the member or following the phone. The native colour scheme is set to
 * match, so the system tab bar and Liquid Glass render dark for Midnight and light for Rose.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const system = useColorScheme();
  const [preference, setPreference] = useState<PalettePreference>('auto');
  const name = resolvePalette(preference, system === 'light' || system === 'dark' ? system : null);

  useEffect(() => {
    Appearance.setColorScheme(preference === 'auto' ? 'unspecified' : getPalette(name).appearance);
  }, [preference, name]);

  const value = useMemo(() => ({ name, palette: getPalette(name), preference, setPreference }), [name, preference]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeValue {
  const value = useContext(ThemeContext);
  if (!value) throw new Error('useTheme must be used inside ThemeProvider');
  return value;
}
