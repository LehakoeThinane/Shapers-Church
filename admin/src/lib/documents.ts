import { apiBaseUrl, type Schemas } from './api';

type Doc = Schemas['DocumentDto'];

/** The Word file type, for uploads and the "is this a Word file?" check. */
export const DOCX_TYPE = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

export const isWordFile = (name: string) => name.toLowerCase().endsWith('.docx');

/** A document's content address: the current version unless one is named; `download` forces a download. */
export const fileUrl = (id: string, options: { version?: number; download?: boolean } = {}) => {
  const query = new URLSearchParams();
  if (options.version) query.set('version', String(options.version));
  if (options.download) query.set('download', 'true');
  const qs = query.toString();
  return `${apiBaseUrl}/api/admin/documents/files/${id}/content${qs ? `?${qs}` : ''}`;
};

/**
 * Sends one file to the API with progress: either into a library folder (a new file, or a new version when the
 * name matches) or as the next version of a document. Files are private, so they go through the API, never
 * straight to storage.
 */
export function sendDocument(
  target: { libraryId: string; folderId: string | null } | { documentId: string },
  file: Blob,
  fileName: string,
  onProgress: (fraction: number) => void = () => undefined,
): Promise<Doc> {
  const url =
    'documentId' in target
      ? `${apiBaseUrl}/api/admin/documents/files/${target.documentId}/versions`
      : `${apiBaseUrl}/api/admin/documents/libraries/${target.libraryId}/files${target.folderId ? `?folderId=${target.folderId}` : ''}`;
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', url);
    xhr.withCredentials = true;
    xhr.setRequestHeader('X-Shapers-CSRF', '1');
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(e.loaded / e.total);
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText) as Doc);
        return;
      }
      let message = `Saving failed (${xhr.status}).`;
      try {
        const problem = JSON.parse(xhr.responseText) as { detail?: string; title?: string };
        message = problem.detail ?? problem.title ?? message;
      } catch {
        /* not JSON */
      }
      reject(new Error(xhr.status === 413 ? 'Files can be up to 100 MB.' : message));
    };
    xhr.onerror = () => reject(new Error('Saving failed. Check your connection and try again.'));
    const form = new FormData();
    form.append('file', file, fileName);
    xhr.send(form);
  });
}
