import { useState } from 'react';
import { ErrorNote } from './ui';
import type { Schemas } from '../lib/api';
import { uploadFile } from '../lib/upload';

/** A button that uploads one image (JPEG, PNG or WebP) and hands back the stored asset. */
export function ImagePicker({ label, onUploaded }: { label: string; onUploaded: (asset: Schemas['AssetDto']) => void }) {
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  return (
    <div className="stack">
      {progress !== null ? (
        <progress max={1} value={progress} />
      ) : (
        <label className="btn btn-secondary">
          {label}
          <input
            type="file"
            hidden
            accept="image/jpeg,image/png,image/webp"
            onChange={async (e) => {
              const file = e.target.files?.[0];
              if (!file) return;
              setError(null);
              setProgress(0);
              try {
                onUploaded(await uploadFile('Image', file, setProgress));
              } catch (err) {
                setError(err);
              } finally {
                setProgress(null);
              }
            }}
          />
        </label>
      )}
      <ErrorNote error={error} />
    </div>
  );
}
