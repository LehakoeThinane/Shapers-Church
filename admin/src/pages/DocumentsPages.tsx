import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type DragEvent, type FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, apiBaseUrl, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { formatBytes } from '../lib/upload';

type Library = Schemas['LibraryDto'];
type Doc = Schemas['DocumentDto'];
type FolderView = Schemas['FolderViewDto'];

const fileUrl = (id: string, options: { version?: number; download?: boolean } = {}) => {
  const query = new URLSearchParams();
  if (options.version) query.set('version', String(options.version));
  if (options.download) query.set('download', 'true');
  const qs = query.toString();
  return `${apiBaseUrl}/api/admin/documents/files/${id}/content${qs ? `?${qs}` : ''}`;
};

/** A small picture of the kind of file, from its type. */
function kindOf(contentType: string, name: string) {
  const ext = name.split('.').pop()?.toLowerCase() ?? '';
  if (contentType.startsWith('image/')) return '🖼';
  if (contentType === 'application/pdf') return '📕';
  if (['doc', 'docx', 'odt', 'rtf'].includes(ext)) return '📝';
  if (['xls', 'xlsx', 'ods', 'csv'].includes(ext)) return '📊';
  if (['ppt', 'pptx', 'odp'].includes(ext)) return '📽';
  if (contentType.startsWith('audio/')) return '🎵';
  if (contentType.startsWith('video/')) return '🎬';
  return '📄';
}

/** Uploads one file to a library folder with progress. The API checks access; the file never gets a public link. */
function uploadDocument(libraryId: string, folderId: string | null, file: File, onProgress: (fraction: number) => void): Promise<Doc> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    const folder = folderId ? `?folderId=${folderId}` : '';
    xhr.open('POST', `${apiBaseUrl}/api/admin/documents/libraries/${libraryId}/files${folder}`);
    xhr.withCredentials = true;
    xhr.setRequestHeader('X-Shapers-CSRF', '1');
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(e.loaded / e.total);
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText) as Doc);
        return;
      }
      let message = `Upload failed (${xhr.status}).`;
      try {
        const problem = JSON.parse(xhr.responseText) as { detail?: string; title?: string };
        message = problem.detail ?? problem.title ?? message;
      } catch {
        /* not JSON */
      }
      reject(new Error(xhr.status === 413 ? 'Files can be up to 100 MB.' : message));
    };
    xhr.onerror = () => reject(new Error('Upload failed. Check your connection and try again.'));
    const form = new FormData();
    form.append('file', file, file.name);
    xhr.send(form);
  });
}

/** Libraries on the left, the open folder on the right. */
export function DocumentsPage() {
  const { libraryId } = useParams();
  const [params, setParams] = useSearchParams();
  const folderId = params.get('folder');
  const navigate = useNavigate();
  const { data: access } = useAccess();
  const [search, setSearch] = useState('');
  const [creating, setCreating] = useState(false);
  const libraries = useQuery({ queryKey: ['documents', 'libraries'], queryFn: async () => unwrap(await api.GET('/api/admin/documents/libraries')) });
  const current = libraries.data?.find((l) => l.id === libraryId) ?? null;
  const canCreate = can(access, Permissions.documentsManage);

  return (
    <>
      <PageHeader
        title="Documents"
        subtitle="Files shared between staff and leaders. Only people with access to a library can open its files, and every opening is logged."
        actions={
          <TextInput type="search" placeholder="Search all files" aria-label="Search all files" value={search} onChange={(e) => setSearch(e.target.value)} />
        }
      />
      <ErrorNote error={libraries.error} />
      <div className="docs-layout">
        <div className="stack">
          <Card title="Libraries" actions={canCreate && <Button variant="ghost" onClick={() => setCreating(true)}>New</Button>}>
            {libraries.isPending ? (
              <Loading />
            ) : libraries.data?.length === 0 ? (
              <Empty>{canCreate ? 'No libraries yet. Create the first one, e.g. "Worship team" or "Leadership".' : 'No libraries have been shared with you yet.'}</Empty>
            ) : (
              <nav className="docs-libraries" aria-label="Libraries">
                {libraries.data?.map((l) => (
                  <Link key={l.id} to={`/documents/${l.id}`} className={l.id === libraryId ? 'active' : undefined} aria-current={l.id === libraryId ? 'page' : undefined}>
                    <span>{l.isPrivate ? '🔒' : '📁'}</span> {l.name}
                  </Link>
                ))}
              </nav>
            )}
            {creating && (
              <LibraryForm
                onDone={(created) => {
                  setCreating(false);
                  if (created) navigate(`/documents/${created.id}`);
                }}
              />
            )}
          </Card>
          <RecentCard />
        </div>
        <div className="stack">
          {search.trim().length >= 2 ? (
            <SearchResults query={search.trim()} />
          ) : current ? (
            <FolderPanel key={`${current.id}-${folderId ?? ''}`} library={current} folderId={folderId} onOpenFolder={(id) => setParams(id ? { folder: id } : {})} />
          ) : (
            <Card>
              <Empty>{libraries.data?.length ? 'Choose a library on the left.' : ' '}</Empty>
            </Card>
          )}
        </div>
      </div>
    </>
  );
}

function FolderPanel({ library, folderId, onOpenFolder }: { library: Library; folderId: string | null; onOpenFolder: (id: string | null) => void }) {
  const queryClient = useQueryClient();
  const key = ['documents', 'folder', library.id, folderId];
  const view = useQuery({
    queryKey: key,
    queryFn: async () => unwrap(await api.GET('/api/admin/documents/libraries/{id}/folder', { params: { path: { id: library.id }, query: { folderId: folderId ?? undefined } } })),
  });
  const canEdit = library.access !== 'View';
  const [uploads, setUploads] = useState<{ name: string; progress: number; error?: string }[]>([]);
  const [over, setOver] = useState(false);
  const [naming, setNaming] = useState<{ kind: 'new-folder' | 'folder' | 'document'; id?: string; name: string } | null>(null);
  const [versionsOf, setVersionsOf] = useState<string | null>(null);
  const [settings, setSettings] = useState(false);
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['documents'] });
  };

  const upload = async (files: FileList | File[]) => {
    const list = Array.from(files);
    setUploads((u) => [...u.filter((x) => !x.error && x.progress < 1), ...list.map((f) => ({ name: f.name, progress: 0 }))]);
    for (const file of list) {
      try {
        await uploadDocument(library.id, folderId, file, (p) => setUploads((u) => u.map((x) => (x.name === file.name ? { ...x, progress: p } : x))));
        setUploads((u) => u.filter((x) => x.name !== file.name));
      } catch (err) {
        setUploads((u) => u.map((x) => (x.name === file.name ? { ...x, error: err instanceof Error ? err.message : 'Upload failed.' } : x)));
      }
      refresh();
    }
  };

  const rename = useMutation({
    mutationFn: async (n: { kind: 'new-folder' | 'folder' | 'document'; id?: string; name: string }) => {
      if (n.kind === 'new-folder') return unwrap(await api.POST('/api/admin/documents/folders', { body: { libraryId: library.id, parentId: folderId, name: n.name } }));
      if (n.kind === 'folder') return unwrap(await api.POST('/api/admin/documents/folders/{id}/rename', { params: { path: { id: n.id! } }, body: { name: n.name } }));
      return unwrap(await api.POST('/api/admin/documents/files/{id}/rename', { params: { path: { id: n.id! } }, body: { name: n.name } }));
    },
    onSuccess: () => {
      setNaming(null);
      refresh();
    },
  });
  const remove = useMutation({
    mutationFn: async (item: { kind: 'folder' | 'document'; id: string }) =>
      item.kind === 'folder'
        ? unwrap(await api.DELETE('/api/admin/documents/folders/{id}', { params: { path: { id: item.id } } }))
        : unwrap(await api.DELETE('/api/admin/documents/files/{id}', { params: { path: { id: item.id } } })),
    onSuccess: refresh,
  });

  const drop = (e: DragEvent) => {
    e.preventDefault();
    setOver(false);
    if (canEdit && e.dataTransfer.files.length) void upload(e.dataTransfer.files);
  };
  const data: FolderView | undefined = view.data;

  return (
    <Card
      title={
        <nav className="docs-crumbs" aria-label="Folder path">
          <button type="button" onClick={() => onOpenFolder(null)}>
            {library.isPrivate ? '🔒 ' : ''}
            {library.name}
          </button>
          {data?.breadcrumb.map((f) => (
            <span key={f.id}>
              {' › '}
              <button type="button" onClick={() => onOpenFolder(f.id)}>
                {f.name}
              </button>
            </span>
          ))}
        </nav>
      }
      actions={
        <div className="row">
          {library.access === 'Manage' && (
            <Button variant="ghost" onClick={() => setSettings(!settings)}>
              Library settings
            </Button>
          )}
          {canEdit && (
            <>
              <Button variant="ghost" onClick={() => setNaming({ kind: 'new-folder', name: '' })}>
                New folder
              </Button>
              <label className="btn btn-primary">
                Upload
                <input type="file" hidden multiple onChange={(e) => e.target.files && void upload(e.target.files)} />
              </label>
            </>
          )}
        </div>
      }>
      {settings && <LibrarySettings library={library} onClose={() => setSettings(false)} />}
      <ErrorNote error={view.error ?? rename.error ?? remove.error} />
      {naming && (
        <form
          className="row docs-naming"
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            rename.mutate(naming);
          }}>
          <Field label={naming.kind === 'new-folder' ? 'New folder name' : 'New name'}>
            <TextInput autoFocus required maxLength={200} value={naming.name} onChange={(e) => setNaming({ ...naming, name: e.target.value })} />
          </Field>
          <Button variant="primary" type="submit" busy={rename.isPending}>
            {naming.kind === 'new-folder' ? 'Create' : 'Rename'}
          </Button>
          <Button variant="ghost" type="button" onClick={() => setNaming(null)}>
            Cancel
          </Button>
        </form>
      )}
      {uploads.length > 0 && (
        <ul className="docs-uploads">
          {uploads.map((u) => (
            <li key={u.name}>
              <span>{u.name}</span>
              {u.error ? <span className="error">{u.error}</span> : <progress max={1} value={u.progress} />}
            </li>
          ))}
        </ul>
      )}
      <div
        className={`docs-drop${over ? ' drop-zone-over' : ''}`}
        onDragOver={(e) => {
          if (!canEdit || !Array.from(e.dataTransfer.types).includes('Files')) return;
          e.preventDefault();
          setOver(true);
        }}
        onDragLeave={() => setOver(false)}
        onDrop={drop}>
        {view.isPending ? (
          <Loading />
        ) : data && data.folders.length + data.documents.length === 0 ? (
          <Empty>{canEdit ? 'This folder is empty. Drag files here, or use Upload.' : 'This folder is empty.'}</Empty>
        ) : (
          <table className="table docs-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Size</th>
                <th>Updated</th>
                <th>By</th>
                <th aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              {data?.folders.map((f) => (
                <tr key={f.id}>
                  <td>
                    <button type="button" className="docs-name" onClick={() => onOpenFolder(f.id)}>
                      📁 {f.name}
                    </button>
                  </td>
                  <td className="muted">—</td>
                  <td />
                  <td />
                  <td className="docs-actions">
                    {canEdit && (
                      <>
                        <Button variant="ghost" onClick={() => setNaming({ kind: 'folder', id: f.id, name: f.name })}>
                          Rename
                        </Button>
                        <Button variant="ghost" onClick={() => confirm(`Move "${f.name}" and everything in it to the recycle bin?`) && remove.mutate({ kind: 'folder', id: f.id })}>
                          Delete
                        </Button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
              {data?.documents.map((d) => (
                <DocumentRow
                  key={d.id}
                  doc={d}
                  canEdit={canEdit}
                  versionsOpen={versionsOf === d.id}
                  onVersions={() => setVersionsOf(versionsOf === d.id ? null : d.id)}
                  onRename={() => setNaming({ kind: 'document', id: d.id, name: d.name })}
                  onDelete={() => confirm(`Move "${d.name}" to the recycle bin?`) && remove.mutate({ kind: 'document', id: d.id })}
                />
              ))}
            </tbody>
          </table>
        )}
      </div>
    </Card>
  );
}

function DocumentRow({
  doc,
  canEdit,
  versionsOpen,
  onVersions,
  onRename,
  onDelete,
}: {
  doc: Doc;
  canEdit: boolean;
  versionsOpen: boolean;
  onVersions: () => void;
  onRename: () => void;
  onDelete: () => void;
}) {
  return (
    <>
      <tr>
        <td>
          <a className="docs-name" href={fileUrl(doc.id)} target="_blank" rel="noopener">
            {kindOf(doc.contentType, doc.name)} {doc.name}
          </a>
          {doc.version > 1 && (
            <span className="small muted"> · v{doc.version}</span>
          )}
        </td>
        <td className="muted">{formatBytes(doc.sizeBytes)}</td>
        <td className="muted">{formatDateTime(doc.updatedAt)}</td>
        <td className="muted">{doc.updatedBy}</td>
        <td className="docs-actions">
          <a className="btn btn-ghost" href={fileUrl(doc.id, { download: true })}>
            Download
          </a>
          <Button variant="ghost" onClick={onVersions}>
            Versions
          </Button>
          {canEdit && (
            <>
              <Button variant="ghost" onClick={onRename}>
                Rename
              </Button>
              <Button variant="ghost" onClick={onDelete}>
                Delete
              </Button>
            </>
          )}
        </td>
      </tr>
      {versionsOpen && (
        <tr>
          <td colSpan={5}>
            <Versions doc={doc} canEdit={canEdit} />
          </td>
        </tr>
      )}
    </>
  );
}

function Versions({ doc, canEdit }: { doc: Doc; canEdit: boolean }) {
  const queryClient = useQueryClient();
  const versions = useQuery({
    queryKey: ['documents', 'versions', doc.id],
    queryFn: async () => unwrap(await api.GET('/api/admin/documents/files/{id}/versions', { params: { path: { id: doc.id } } })),
  });
  const restore = useMutation({
    mutationFn: async (number: number) => unwrap(await api.POST('/api/admin/documents/files/{id}/versions/{number}/restore', { params: { path: { id: doc.id, number } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['documents'] }),
  });
  return (
    <div className="docs-versions">
      <ErrorNote error={versions.error ?? restore.error} />
      {versions.data?.map((v) => (
        <div key={v.number} className="row">
          <span>
            <strong>v{v.number}</strong> · {formatDateTime(v.uploadedAt)} · {v.uploadedBy} · {formatBytes(v.sizeBytes)}
          </span>
          {v.isCurrent ? (
            <Badge tone="success">Current</Badge>
          ) : (
            <>
              <a className="btn btn-ghost" href={fileUrl(doc.id, { version: v.number, download: true })}>
                Download
              </a>
              {canEdit && (
                <Button variant="ghost" busy={restore.isPending && restore.variables === v.number} onClick={() => restore.mutate(v.number)}>
                  Make current
                </Button>
              )}
            </>
          )}
        </div>
      ))}
    </div>
  );
}

function LibraryForm({ onDone }: { onDone: (created: Library | null) => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [isPrivate, setPrivate] = useState(false);
  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/documents/libraries', { body: { name, scope: null, isPrivate } })),
    onSuccess: (l) => {
      void queryClient.invalidateQueries({ queryKey: ['documents', 'libraries'] });
      onDone(l);
    },
  });
  return (
    <form
      className="stack docs-new-library"
      onSubmit={(e: FormEvent) => {
        e.preventDefault();
        create.mutate();
      }}>
      <Field label="Library name">
        <TextInput autoFocus required maxLength={200} placeholder="e.g. Worship team" value={name} onChange={(e) => setName(e.target.value)} />
      </Field>
      <label className="checkbox">
        <input type="checkbox" checked={isPrivate} onChange={(e) => setPrivate(e.target.checked)} />
        Private: only people I choose
      </label>
      <ErrorNote error={create.error} />
      <div className="row">
        <Button variant="primary" type="submit" busy={create.isPending}>
          Create
        </Button>
        <Button variant="ghost" type="button" onClick={() => onDone(null)}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

/** Managers: rename, make private, choose who's in a private library, or delete an empty one. */
function LibrarySettings({ library, onClose }: { library: Library; onClose: () => void }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [name, setName] = useState(library.name);
  const [isPrivate, setPrivate] = useState(library.isPrivate);
  const [search, setSearch] = useState('');
  const members = useQuery({
    queryKey: ['documents', 'members', library.id],
    queryFn: async () => unwrap(await api.GET('/api/admin/documents/libraries/{id}/members', { params: { path: { id: library.id } } })),
  });
  const [list, setList] = useState<Schemas['MemberDto'][] | null>(null);
  const shown = list ?? members.data ?? [];
  const people = useQuery({
    queryKey: ['documents', 'people', search],
    enabled: search.trim().length >= 2,
    queryFn: async () => unwrap(await api.GET('/api/admin/people', { params: { query: { Search: search.trim(), PageSize: 8 } } })),
  });
  const save = useMutation({
    mutationFn: async () => {
      await unwrap(await api.PUT('/api/admin/documents/libraries/{id}', { params: { path: { id: library.id } }, body: { name, scope: null, isPrivate } }));
      if (isPrivate) {
        await unwrap(
          await api.PUT('/api/admin/documents/libraries/{id}/members', {
            params: { path: { id: library.id } },
            body: shown.map((m) => ({ personId: m.personId, canEdit: m.canEdit })),
          }),
        );
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
      onClose();
    },
  });
  const remove = useMutation({
    mutationFn: async () => unwrap(await api.DELETE('/api/admin/documents/libraries/{id}', { params: { path: { id: library.id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
      navigate('/documents');
    },
  });

  return (
    <div className="docs-settings stack">
      <div className="row">
        <Field label="Name">
          <TextInput maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field label="Who can use it">
          <Select value={isPrivate ? 'private' : 'shared'} onChange={(e) => setPrivate(e.target.value === 'private')}>
            <option value="shared">Everyone with documents access</option>
            <option value="private">Only the people listed below</option>
          </Select>
        </Field>
      </div>
      {isPrivate && (
        <div className="stack">
          {shown.length === 0 && <p className="small muted">No one yet. Document managers can always open it.</p>}
          {shown.map((m) => (
            <div key={m.personId} className="row">
              <span>{m.name}</span>
              <label className="checkbox">
                <input type="checkbox" checked={m.canEdit} onChange={(e) => setList(shown.map((x) => (x.personId === m.personId ? { ...x, canEdit: e.target.checked } : x)))} />
                Can change files
              </label>
              <Button variant="ghost" onClick={() => setList(shown.filter((x) => x.personId !== m.personId))}>
                Remove
              </Button>
            </div>
          ))}
          <Field label="Add someone">
            <TextInput type="search" placeholder="Start typing a name" value={search} onChange={(e) => setSearch(e.target.value)} />
          </Field>
          {people.data?.items
            .filter((p) => !shown.some((m) => m.personId === p.id))
            .map((p) => (
              <Button
                key={p.id}
                variant="ghost"
                onClick={() => {
                  setList([...shown, { personId: p.id, name: p.displayName, canEdit: false }]);
                  setSearch('');
                }}>
                + {p.displayName}
              </Button>
            ))}
        </div>
      )}
      <ErrorNote error={save.error ?? remove.error ?? members.error} />
      <div className="row">
        <Button variant="primary" busy={save.isPending} onClick={() => save.mutate()}>
          Save
        </Button>
        <Button variant="ghost" onClick={onClose}>
          Close
        </Button>
        <Button variant="danger" busy={remove.isPending} onClick={() => confirm(`Delete the library "${library.name}"? It has to be empty.`) && remove.mutate()}>
          Delete library
        </Button>
      </div>
    </div>
  );
}

function RecentCard() {
  const recent = useQuery({ queryKey: ['documents', 'recent'], queryFn: async () => unwrap(await api.GET('/api/admin/documents/recent')) });
  if (!recent.data?.length) return null;
  return (
    <Card title="Recent">
      <ul className="docs-hits">
        {recent.data.slice(0, 8).map((h) => (
          <li key={h.document.id}>
            <a href={fileUrl(h.document.id)} target="_blank" rel="noopener">
              {kindOf(h.document.contentType, h.document.name)} {h.document.name}
            </a>
            <span className="small muted">
              {h.libraryName}
              {h.path && ` / ${h.path}`} · {formatDateTime(h.document.updatedAt)}
            </span>
          </li>
        ))}
      </ul>
    </Card>
  );
}

function SearchResults({ query }: { query: string }) {
  const hits = useQuery({
    queryKey: ['documents', 'search', query],
    queryFn: async () => unwrap(await api.GET('/api/admin/documents/search', { params: { query: { q: query } } })),
  });
  return (
    <Card title={`Files matching "${query}"`}>
      <ErrorNote error={hits.error} />
      {hits.isPending ? (
        <Loading />
      ) : hits.data?.length === 0 ? (
        <Empty>Nothing found in the libraries you can open.</Empty>
      ) : (
        <ul className="docs-hits">
          {hits.data?.map((h) => (
            <li key={h.document.id}>
              <a href={fileUrl(h.document.id)} target="_blank" rel="noopener">
                {kindOf(h.document.contentType, h.document.name)} {h.document.name}
              </a>
              <span className="small muted">
                <Link to={`/documents/${h.libraryId}${h.document.folderId ? `?folder=${h.document.folderId}` : ''}`}>
                  {h.libraryName}
                  {h.path && ` / ${h.path}`}
                </Link>{' '}
                · {formatBytes(h.document.sizeBytes)} · {formatDateTime(h.document.updatedAt)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

export function DocumentsBinPage() {
  const queryClient = useQueryClient();
  const bin = useQuery({ queryKey: ['documents', 'bin'], queryFn: async () => unwrap(await api.GET('/api/admin/documents/recycle-bin')) });
  const restore = useMutation({
    mutationFn: async (item: Schemas['BinItemDto']) =>
      unwrap(await api.POST('/api/admin/documents/recycle-bin/{kind}/{id}/restore', { params: { path: { kind: item.kind, id: item.id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['documents'] }),
  });
  return (
    <>
      <PageHeader title="Recycle bin" subtitle="Deleted files and folders from libraries you can change. They're removed for good after 30 days." />
      <Card>
        <ErrorNote error={bin.error ?? restore.error} />
        {bin.isPending ? (
          <Loading />
        ) : bin.data?.length === 0 ? (
          <Empty>The recycle bin is empty.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Library</th>
                <th>Deleted</th>
                <th aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              {bin.data?.map((i) => (
                <tr key={`${i.kind}-${i.id}`}>
                  <td>
                    {i.kind === 'folder' ? '📁' : '📄'} {i.name}
                  </td>
                  <td className="muted">{i.libraryName}</td>
                  <td className="muted">{formatDateTime(i.deletedAt)}</td>
                  <td className="docs-actions">
                    <Button variant="ghost" busy={restore.isPending && restore.variables?.id === i.id} onClick={() => restore.mutate(i)}>
                      Restore
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}
