import { useEffect, useState } from 'react';
import { resolvePalette, type PalettePreference } from '@shapers/tokens';

/**
 * Admin portal shell (Phase 0, in progress): applies the palette and shows the church the API reports.
 * Sign-in, navigation and the People screens build on this.
 */
export default function App() {
  const [preference] = useState<PalettePreference>('auto');
  const [church, setChurch] = useState<string>('Loading…');

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: light)');
    const apply = () => {
      document.documentElement.dataset.palette = resolvePalette(preference, media.matches ? 'light' : 'dark');
    };
    apply();
    media.addEventListener('change', apply);
    return () => media.removeEventListener('change', apply);
  }, [preference]);

  useEffect(() => {
    fetch('/api/church')
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then((data: { organisation: { name: string } }) => setChurch(data.organisation.name))
      .catch(() => setChurch('API not reachable'));
  }, []);

  return (
    <main className="shell">
      <section className="glass card">
        <h1>Shapers admin</h1>
        <p>{church}</p>
      </section>
    </main>
  );
}
