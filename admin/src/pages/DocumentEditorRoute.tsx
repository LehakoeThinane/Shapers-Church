import { lazy, Suspense } from 'react';
import { Loading } from '../components/ui';

// The Word editor (TipTap, mammoth, docx) is large: it loads only when someone opens a document in it.
const DocumentEditorPage = lazy(() => import('./DocumentEditorPage'));

export function DocumentEditorRoute() {
  return (
    <Suspense fallback={<Loading />}>
      <DocumentEditorPage />
    </Suspense>
  );
}