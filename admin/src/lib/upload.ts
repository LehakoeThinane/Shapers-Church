import { api, unwrap, type Schemas } from './api';

export type MediaKind = Schemas['MediaKind'];

/** Reads an audio file's length in the browser, so listeners see durations before playing. */
export function audioDuration(file: File): Promise<number | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const audio = new Audio();
    audio.preload = 'metadata';
    audio.onloadedmetadata = () => {
      URL.revokeObjectURL(url);
      resolve(Number.isFinite(audio.duration) ? Math.round(audio.duration) : null);
    };
    audio.onerror = () => {
      URL.revokeObjectURL(url);
      resolve(null);
    };
    audio.src = url;
  });
}

/**
 * Uploads a file straight to storage: reserve a slot, PUT the bytes to the signed URL (with progress),
 * then confirm. The file never passes through the API server.
 */
export async function uploadFile(kind: MediaKind, file: File, onProgress: (fraction: number) => void): Promise<Schemas['AssetDto']> {
  const duration = kind === 'Audio' ? await audioDuration(file) : null;
  const start = unwrap(
    await api.POST('/api/admin/media/uploads', {
      body: { kind, fileName: file.name, contentType: file.type || 'application/octet-stream', sizeBytes: file.size },
    }),
  );

  await new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open(start.upload.method, start.upload.url);
    xhr.withCredentials = new URL(start.upload.url, window.location.href).origin === window.location.origin;
    for (const [header, value] of Object.entries(start.upload.headers)) {
      xhr.setRequestHeader(header, value);
    }

    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(e.loaded / e.total);
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error(`Upload failed (${xhr.status}).`)));
    xhr.onerror = () => reject(new Error('Upload failed. Check your connection and try again.'));
    xhr.send(file);
  });

  onProgress(1);
  return unwrap(
    await api.POST('/api/admin/media/uploads/{id}/complete', { params: { path: { id: start.assetId } }, body: { durationSeconds: duration } }),
  );
}

export function formatDuration(seconds: number | null | undefined): string {
  if (!seconds) return '';
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return h > 0 ? `${h} h ${m} min` : `${m} min`;
}

export function formatBytes(bytes: number): string {
  return bytes > 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
}
