import { isPalettePreference, resolvePalette, type PalettePreference } from '@shapers/tokens';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from './api';
import { PaletteContext } from './palette-context';

const STORAGE_KEY = 'shapers.admin.palette';

function readStored(): PalettePreference {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return isPalettePreference(value) ? value : 'auto';
  } catch {
    return 'auto';
  }
}

function systemScheme(): 'light' | 'dark' {
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

/** Midnight or Rose on the <html> element; "auto" follows the browser. Synced to the account when signed in. */
export function PaletteProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<PalettePreference>(readStored);
  const [scheme, setScheme] = useState(systemScheme);

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: light)');
    const onChange = () => setScheme(systemScheme());
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, []);

  const palette = resolvePalette(preference, scheme);
  useEffect(() => {
    document.documentElement.dataset.palette = palette;
  }, [palette]);

  const setPreference = useCallback((value: PalettePreference, syncToAccount = true) => {
    setPreferenceState(value);
    try {
      window.localStorage.setItem(STORAGE_KEY, value);
    } catch {
      // Private browsing: the choice lasts for this tab only.
    }

    if (syncToAccount) {
      void api.PUT('/api/me/preferences', { body: { palette: value } });
    }
  }, []);

  const value = useMemo(() => ({ preference, palette, setPreference }), [preference, palette, setPreference]);
  return <PaletteContext.Provider value={value}>{children}</PaletteContext.Provider>;
}
