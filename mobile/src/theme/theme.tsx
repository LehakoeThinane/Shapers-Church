import AsyncStorage from '@react-native-async-storage/async-storage';
import { getPalette, resolvePalette, type Palette, type PaletteName, type PalettePreference } from '@shapers/tokens';
import { createContext, useContext, useEffect, useMemo, type ReactNode } from 'react';
import { Appearance, Platform, useColorScheme } from 'react-native';
import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

interface PaletteState {
  preference: PalettePreference;
  setPreference: (preference: PalettePreference) => void;
}

/** The member's palette choice, remembered on the device. Synced to their account when signed in (see profile). */
export const usePaletteStore = create<PaletteState>()(
  persist(
    (set) => ({
      preference: 'auto',
      setPreference: (preference) => set({ preference }),
    }),
    { name: 'shapers.palette', storage: createJSONStorage(() => AsyncStorage) },
  ),
);

interface ThemeValue {
  name: PaletteName;
  palette: Palette;
  preference: PalettePreference;
  setPreference: (preference: PalettePreference) => void;
}

const ThemeContext = createContext<ThemeValue | null>(null);

/**
 * Midnight or Rose. The native colour scheme is set to match the palette, so the system tab bar
 * and Liquid Glass render dark for Midnight and light for Rose.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const system = useColorScheme();
  const { preference, setPreference } = usePaletteStore();
  const name = resolvePalette(preference, system === 'light' || system === 'dark' ? system : null);

  useEffect(() => {
    // Not available in the browser preview, which has no system tab bar or glass to match.
    if (Platform.OS === 'web' || typeof Appearance.setColorScheme !== 'function') return;
    Appearance.setColorScheme(preference === 'auto' ? 'unspecified' : getPalette(name).appearance);
  }, [preference, name]);

  const value = useMemo(() => ({ name, palette: getPalette(name), preference, setPreference }), [name, preference, setPreference]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeValue {
  const value = useContext(ThemeContext);
  if (!value) throw new Error('useTheme must be used inside ThemeProvider');
  return value;
}
