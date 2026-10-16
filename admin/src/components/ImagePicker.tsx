import { useRef, useState, type DragEvent, type TextareaHTMLAttributes } from 'react';
import { ErrorNote } from './ui';
import type { Schemas } from '../lib/api';
import { uploadFile } from '../lib/upload';

const imageTypes = ['image/jpeg', 'image/png', 'image/webp'];

/** The first JPEG, PNG or WebP in a drop or paste, if any. */
function imageFrom(files: FileList | null | undefined): File | undefined {
  return Array.from(files ?? []).find((f) => imageTypes.includes(f.type));
}

const hasFiles = (e: DragEvent) => Array.from(e.dataTransfer.types).includes('Files');

/** A drop zone and button that upload one image (JPEG, PNG or WebP) and hand back the stored asset. */
export function ImagePicker({ label, onUploaded }: { label: string; onUploaded: (asset: Schemas['AssetDto']) => void }) {
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [over, setOver] = useState(false);

  const upload = async (file: File | undefined) => {
    if (!file) {
      setError(new Error('Choose a JPEG, PNG or WebP picture.'));
      return;
    }
    setError(null);
    setProgress(0);
    try {
      onUploaded(await uploadFile('Image', file, setProgress));
    } catch (err) {
      setError(err);
    } finally {
      setProgress(null);
    }
  };

  return (
    <div className="stack">
      <div
        className={`drop-zone${over ? ' drop-zone-over' : ''}`}
        onDragOver={(e) => {
          if (!hasFiles(e)) return;
          e.preventDefault();
          setOver(true);
        }}
        onDragLeave={() => setOver(false)}
        onDrop={(e) => {
          e.preventDefault();
          setOver(false);
          if (progress === null) void upload(imageFrom(e.dataTransfer.files));
        }}>
        {progress !== null ? (
          <progress max={1} value={progress} />
        ) : (
          <>
            <label className="btn btn-secondary">
              {label}
              <input type="file" hidden accept={imageTypes.join(',')} onChange={(e) => void upload(e.target.files?.[0])} />
            </label>
            <span className="small muted">or drag a picture here</span>
          </>
        )}
      </div>
      <ErrorNote error={error} />
    </div>
  );
}

/**
 * A Markdown text box that takes pictures: drop or paste one and it uploads, then appears where the cursor is
 * as ![](link).
 */
export function MarkdownImageArea({
  value,
  onChange,
  ...props
}: Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'value' | 'onChange'> & { value: string; onChange: (value: string) => void }) {
  const ref = useRef<HTMLTextAreaElement>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [over, setOver] = useState(false);

  const insert = async (file: File) => {
    const box = ref.current;
    const at = box?.selectionStart ?? value.length;
    setError(null);
    setProgress(0);
    try {
      const asset = await uploadFile('Image', file, setProgress);
      const alt = file.name.replace(/\.[^.]+$/, '').replace(/[[\]]/g, '');
      const before = value.slice(0, at);
      const after = value.slice(at);
      const gap = before && !before.endsWith('\n\n') ? (before.endsWith('\n') ? '\n' : '\n\n') : '';
      onChange(`${before}${gap}![${alt}](${asset.url})\n\n${after.replace(/^\n+/, '')}`);
    } catch (err) {
      setError(err);
    } finally {
      setProgress(null);
    }
  };

  return (
    <div className="stack">
      <textarea
        {...props}
        ref={ref}
        className={`input${over ? ' drop-zone-over' : ''}`}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onDragOver={(e) => {
          if (!hasFiles(e)) return;
          e.preventDefault();
          setOver(true);
        }}
        onDragLeave={() => setOver(false)}
        onDrop={(e) => {
          if (!hasFiles(e)) return;
          e.preventDefault();
          setOver(false);
          const file = imageFrom(e.dataTransfer.files);
          if (file && progress === null) void insert(file);
          else if (!file) setError(new Error('Only JPEG, PNG or WebP pictures can be added here.'));
        }}
        onPaste={(e) => {
          const file = imageFrom(e.clipboardData.files);
          if (!file || progress !== null) return;
          e.preventDefault();
          void insert(file);
        }}
      />
      {progress !== null ? (
        <progress max={1} value={progress} aria-label="Uploading picture" />
      ) : (
        <span className="small muted">Drag a picture into the text, or paste one, to add it where the cursor is.</span>
      )}
      <ErrorNote error={error} />
    </div>
  );
}
