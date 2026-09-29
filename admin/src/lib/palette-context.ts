import type { PaletteName, PalettePreference } from '@shapers/tokens';
import { createContext, useContext } from 'react';

export interface PaletteValue {
  preference: PalettePreference;
  palette: PaletteName;
  setPreference: (value: PalettePreference, syncToAccount?: boolean) => void;
}

export const PaletteContext = createContext<PaletteValue | null>(null);

export function usePalette(): PaletteValue {
  const value = useContext(PaletteContext);
  if (!value) throw new Error('usePalette must be used inside PaletteProvider');
  return value;
}
