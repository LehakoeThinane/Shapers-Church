import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import Markdown from 'react-markdown';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { AiBadge } from '../components/Assist';
import { useAssistStatus, useCanDraft, useReviewDraft, type Draft } from '../lib/assist';
import { formatBytes, formatDuration, uploadFile, type MediaKind } from '../lib/upload';

type Admin = Schemas['SermonAdminDto'];

interface FormState {
  title: string;
  preachedOn: string;
  seriesId: string;
  summary: string;
  notes: string;
  topics: string;
  speakerIds: string[];
  scripture: string;
  videoUrl: string;
}

const emptyForm = (): FormState => ({
  title: '',
  preachedOn: new Date().toISOString().slice(0, 10),
  seriesId: '',
  summary: '',
  notes: '',
  topics: '',
  speakerIds: [],
  scripture: '',
  videoUrl: '',
});

function toForm(a: Admin): FormState {
  const s = a.sermon;
  return {
    title: s.title,
    preachedOn: s.preachedOn,
    seriesId: s.series?.id ?? '',
    summary: s.summary ?? '',
    notes: s.notes ?? '',
    topics: s.topics.join(', '),
    speakerIds: s.speakers.map((x) => x.id),
    scripture: s.scripture.map((x) => x.display.replace('–', '-')).join('; '),
    videoUrl: s.video?.watchUrl ?? '',
  };
}

function toRequest(f: FormState): Schemas['SaveSermonRequest'] {
  return {
    title: f.title,
    preachedOn: f.preachedOn,
    seriesId: f.seriesId || null,
    summary: f.summary || null,
    notes: f.notes || null,
    topics: f.topics.split(',').map((t) => t.trim()).filter(Boolean),
    speakerIds: f.speakerIds,
    scripture: f.scripture || null,
    videoUrl: f.videoUrl || null,
    scope: null,
  };
}

export function SermonEditorPage() {
  const { id } = useParams();
  const isNew = !id;
  const sermon = useQuery({
    queryKey: ['admin-sermon', id],
    queryFn: async () => unwrap(await api.GET('/api/admin/media/sermons/{id}', { params: { path: { id: id! } } })),
    enabled: !isNew,
  });

  if (!isNew && sermon.isPending) return <Loading />;
  if (sermon.error) return <ErrorNote error={sermon.error} />;
  return <Editor key={sermon.data?.updatedAt ?? 'new'} existing={sermon.data ?? null} />;
}

function Editor({ existing }: { existing: Admin | null }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const [form, setForm] = useState<FormState>(existing ? toForm(existing) : emptyForm());
  const [preview, setPreview] = useState(false);
  const set = (patch: Partial<FormState>) => setForm({ ...form, ...patch });

  const speakers = useQuery({ queryKey: ['speakers'], queryFn: async () => unwrap(await api.GET('/api/admin/media/speakers')) });
  const series = useQuery({ queryKey: ['admin-series'], queryFn: async () => unwrap(await api.GET('/api/admin/media/series')) });
  const scripture = useScriptureCheck(form.scripture);

  const saved = (a: Admin) => {
    queryClient.setQueryData(['admin-sermon', a.sermon.id], a);
    void queryClient.invalidateQueries({ queryKey: ['admin-sermons'] });
    if (!existing) navigate(`/sermons/${a.sermon.id}`, { replace: true });
  };

  const save = useMutation({
    mutationFn: async () =>
      existing
        ? unwrap(await api.PUT('/api/admin/media/sermons/{id}', { params: { path: { id: existing.sermon.id } }, body: toRequest(form) }))
        : unwrap(await api.POST('/api/admin/media/sermons', { body: toRequest(form) })),
    onSuccess: saved,
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  const video = /(?:v=|youtu\.be\/|embed\/|live\/)([A-Za-z0-9_-]{11})/.exec(form.videoUrl)?.[1] ?? (/^[A-Za-z0-9_-]{11}$/.test(form.videoUrl) ? form.videoUrl : null);

  return (
    <>
      <PageHeader
        title={existing ? existing.sermon.title : 'New sermon'}
        subtitle={<Link to="/sermons">Back to sermons</Link>}
        actions={existing && <StatusBadge sermon={existing} />}
      />

      <div className="grid-2">
        <Card title="Details">
          <form className="stack" onSubmit={submit}>
            <Field label="Title">
              <TextInput required maxLength={200} value={form.title} onChange={(e) => set({ title: e.target.value })} />
            </Field>
            <div className="form-grid">
              <Field label="Preached on">
                <TextInput type="date" required value={form.preachedOn} onChange={(e) => set({ preachedOn: e.target.value })} />
              </Field>
              <Field label="Series">
                <Select value={form.seriesId} onChange={(e) => set({ seriesId: e.target.value })}>
                  <option value="">No series</option>
                  {series.data?.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.title}
                    </option>
                  ))}
                </Select>
              </Field>
            </div>

            <Field label="Speakers" hint={speakers.data?.length === 0 ? 'Add speakers under Series & speakers first.' : undefined}>
              <div className="checks">
                {speakers.data?.map((s) => (
                  <label key={s.id} className="checkbox">
                    <input
                      type="checkbox"
                      checked={form.speakerIds.includes(s.id)}
                      onChange={(e) =>
                        set({ speakerIds: e.target.checked ? [...form.speakerIds, s.id] : form.speakerIds.filter((x) => x !== s.id) })
                      }
                    />
                    {s.name} {s.title && <span className="muted small">{s.title}</span>}
                  </label>
                ))}
              </div>
            </Field>

            <Field label="Scripture" hint="Separate passages with semicolons, e.g. Isaiah 42:1-9; Matthew 12:18-21">
              <TextInput value={form.scripture} onChange={(e) => set({ scripture: e.target.value })} />
            </Field>
            {scripture.data && (scripture.data.recognised.length > 0 || scripture.data.unrecognised.length > 0) && (
              <p className="small">
                {scripture.data.recognised.map((r) => (
                  <Badge key={r} tone="success">
                    {r}
                  </Badge>
                ))}
                {scripture.data.unrecognised.map((r) => (
                  <Badge key={r} tone="danger">
                    Can't read "{r}"
                  </Badge>
                ))}
              </p>
            )}

            <Field label="YouTube link">
              <TextInput placeholder="https://www.youtube.com/watch?v=…" value={form.videoUrl} onChange={(e) => set({ videoUrl: e.target.value })} />
            </Field>
            {video && <img className="thumb" src={`https://i.ytimg.com/vi/${video}/mqdefault.jpg`} alt="Video thumbnail" width={240} height={135} />}

            <Field label="Summary" hint="One or two sentences for lists, podcasts and link previews.">
              <textarea className="input" rows={2} maxLength={1000} value={form.summary} onChange={(e) => set({ summary: e.target.value })} />
            </Field>
            <Field label="Topics" hint="Comma separated, e.g. Prayer, Servanthood">
              <TextInput value={form.topics} onChange={(e) => set({ topics: e.target.value })} />
            </Field>

            <div className="row">
              <strong className="small">Notes</strong>
              <button type="button" className="link-button" onClick={() => setPreview(!preview)}>
                {preview ? 'Edit' : 'Preview'}
              </button>
            </div>
            {preview ? (
              <div className="markdown">{form.notes ? <Markdown>{form.notes}</Markdown> : <p className="muted">No notes yet.</p>}</div>
            ) : (
              <textarea
                className="input"
                rows={10}
                placeholder={'# Main point\n- Supporting point\n\n> Scripture quote'}
                value={form.notes}
                onChange={(e) => set({ notes: e.target.value })}
              />
            )}

            <ErrorNote error={save.error} />
            <Button variant="primary" type="submit" busy={save.isPending}>
              {existing ? 'Save changes' : 'Create draft'}
            </Button>
          </form>
        </Card>

        {existing && (
          <div className="stack">
            <PublishCard sermon={existing} canPublish={can(access, Permissions.mediaPublish)} onChange={saved} />
            <MediaCard sermon={existing} kind="Audio" onChange={saved} />
            <TranscriptCard sermon={existing} onChange={saved} />
            <SermonAssistCard
              sermon={existing}
              onUseNotes={(n) => set({ summary: n.summary, notes: n.notes, topics: n.topics.join(', ') })}
            />
            <MediaCard sermon={existing} kind="NotesPdf" onChange={saved} />
          </div>
        )}
      </div>
    </>
  );
}

const transcriptLabel: Record<Schemas['TranscriptStatus'], string> = {
  None: 'No transcript yet',
  Queued: 'Waiting to be transcribed',
  Working: 'Transcribing…',
  Ready: 'Ready',
  Failed: 'Transcription failed',
};

/** The sermon's words as text: transcribed from the audio, or pasted. Staff-only; AI drafting works from it. */
function TranscriptCard({ sermon, onChange }: { sermon: Admin; onChange: (a: Admin) => void }) {
  const id = sermon.sermon.id;
  const info = sermon.transcript;
  const { data: status } = useAssistStatus();
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState('');
  const queryClient = useQueryClient();
  const busy = info.status === 'Queued' || info.status === 'Working';

  // While the job runs (every few minutes), check back so the card updates by itself.
  useQuery({
    queryKey: ['admin-sermon', id, 'transcript-poll'],
    queryFn: async () => {
      const latest = unwrap(await api.GET('/api/admin/media/sermons/{id}', { params: { path: { id } } }));
      if (latest.transcript.status !== info.status) onChange(latest);
      return latest.transcript.status;
    },
    enabled: busy,
    refetchInterval: 30_000,
  });

  const open = useMutation({
    mutationFn: async () => unwrap(await api.GET('/api/admin/media/sermons/{id}/transcript', { params: { path: { id } } })),
    onSuccess: (t) => {
      setText(t.text ?? '');
      setEditing(true);
    },
  });
  const save = useMutation({
    mutationFn: async () => unwrap(await api.PUT('/api/admin/media/sermons/{id}/transcript', { params: { path: { id } }, body: { text: text || null } })),
    onSuccess: (a) => {
      setEditing(false);
      void queryClient.invalidateQueries({ queryKey: ['assist'] });
      onChange(a);
    },
  });
  const transcribe = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/media/sermons/{id}/transcript/transcribe', { params: { path: { id } } })),
    onSuccess: onChange,
  });
  const youtube = useQuery({ queryKey: ['youtube-connection'], queryFn: async () => unwrap(await api.GET('/api/admin/media/youtube')) });
  const captions = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/media/sermons/{id}/transcript/captions', { params: { path: { id } } })),
    onSuccess: onChange,
  });

  return (
    <Card
      title="Transcript"
      actions={
        <Badge tone={info.status === 'Ready' ? 'success' : info.status === 'Failed' ? 'danger' : 'neutral'}>{transcriptLabel[info.status]}</Badge>
      }
    >
      {info.status === 'Ready' && (
        <p className="small muted">
          {info.length.toLocaleString('en-ZA')} characters, {info.source === 'Audio' ? 'transcribed from the audio' : info.source === 'Captions' ? 'from YouTube captions' : 'pasted'}
          {info.updatedAt && `, ${formatDateTime(info.updatedAt)}`}. Only staff see it.
        </p>
      )}
      {info.status === 'Failed' && info.error && <p className="note note-danger small">{info.error}</p>}
      {busy && <p className="small muted">This takes a few minutes after the upload. You can leave this page.</p>}
      {info.status === 'None' && <p className="small muted">A transcript lets AI help draft show notes and a cell lesson from this sermon.</p>}

      {editing ? (
        <div className="stack">
          <textarea className="input" rows={12} value={text} onChange={(e) => setText(e.target.value)} placeholder="Paste the sermon's words here." />
          <div className="row">
            <Button variant="primary" busy={save.isPending} onClick={() => save.mutate()}>
              Save transcript
            </Button>
            <Button variant="ghost" onClick={() => setEditing(false)}>
              Cancel
            </Button>
          </div>
        </div>
      ) : (
        <div className="row">
          <Button busy={open.isPending} disabled={busy} onClick={() => open.mutate()}>
            {info.status === 'Ready' ? 'View or edit' : 'Paste transcript'}
          </Button>
          {status?.enabled && sermon.sermon.audio && !busy && (
            <Button busy={transcribe.isPending} onClick={() => transcribe.mutate()}>
              {info.status === 'Ready' ? 'Transcribe audio again' : 'Transcribe audio'}
            </Button>
          )}
          {youtube.data?.connected && sermon.sermon.video && !busy && info.status !== 'Ready' && (
            <Button busy={captions.isPending} onClick={() => captions.mutate()}>
              Get captions from YouTube
            </Button>
          )}
        </div>
      )}
      <ErrorNote error={open.error ?? save.error ?? transcribe.error ?? captions.error} />
    </Card>
  );
}

/** AI drafts from the transcript: show notes for this page, or a lesson for every cell. */
function SermonAssistCard({ sermon, onUseNotes }: { sermon: Admin; onUseNotes: (notes: Schemas['NotesDraft']) => void }) {
  const navigate = useNavigate();
  const canDraft = useCanDraft();
  const review = useReviewDraft();
  const [notes, setNotes] = useState<Draft | null>(null);
  const body = { sermonId: sermon.sermon.id };
  const draftNotes = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/assist/drafts/sermon-notes', { body })),
    onSuccess: setNotes,
  });
  const draftLesson = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/assist/drafts/sermon-lesson', { body })),
    onSuccess: (d) => navigate(`/cells/lessons?draft=${d.id}`),
  });

  if (!canDraft) return null;
  const ready = sermon.transcript.status === 'Ready';
  return (
    <Card title="AI help">
      {!ready ? (
        <p className="small muted">Add a transcript first. AI works only from what was preached.</p>
      ) : (
        <div className="stack">
          <p className="small muted">AI writes a first draft from the transcript. Check it against the sermon and change anything that isn't right.</p>
          <div className="row">
            <Button busy={draftNotes.isPending} disabled={draftLesson.isPending} onClick={() => draftNotes.mutate()}>
              Draft summary and notes
            </Button>
            <Button busy={draftLesson.isPending} disabled={draftNotes.isPending} onClick={() => draftLesson.mutate()}>
              Draft a cell lesson
            </Button>
          </div>
          {(draftNotes.isPending || draftLesson.isPending) && <p className="small muted">Writing… this can take up to a minute.</p>}
        </div>
      )}
      <ErrorNote error={draftNotes.error ?? draftLesson.error} />
      {notes?.notes && (
        <div className="ai-suggestion stack">
          <span>
            <AiBadge />
          </span>
          <p>
            <strong>Summary:</strong> {notes.notes.summary}
          </p>
          <p className="small">
            <strong>Topics:</strong> {notes.notes.topics.join(', ')}
          </p>
          <div className="markdown">
            <Markdown>{notes.notes.notes}</Markdown>
          </div>
          <div className="row">
            <Button
              variant="primary"
              onClick={() => {
                onUseNotes(notes.notes!);
                review.mutate({ id: notes.id, action: 'accept' });
                setNotes(null);
              }}
            >
              Put in the form
            </Button>
            <Button
              variant="ghost"
              onClick={() => {
                review.mutate({ id: notes.id, action: 'discard' });
                setNotes(null);
              }}
            >
              Discard
            </Button>
          </div>
          <p className="small muted">Nothing is saved until you press Save changes.</p>
        </div>
      )}
    </Card>
  );
}

function useScriptureCheck(text: string) {
  const [debounced, setDebounced] = useState(text);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(text), 400);
    return () => clearTimeout(t);
  }, [text]);
  return useQuery({
    queryKey: ['scripture-check', debounced],
    queryFn: async () => unwrap(await api.GET('/api/admin/media/scripture-check', { params: { query: { text: debounced } } })),
    enabled: debounced.trim().length > 0,
    staleTime: Infinity,
  });
}

function StatusBadge({ sermon }: { sermon: Admin }) {
  const tone = sermon.status === 'Published' ? 'success' : sermon.status === 'Archived' ? 'danger' : sermon.status === 'Scheduled' ? 'accent' : 'neutral';
  return <Badge tone={tone}>{sermon.status}</Badge>;
}

function PublishCard({ sermon, canPublish, onChange }: { sermon: Admin; canPublish: boolean; onChange: (a: Admin) => void }) {
  const [when, setWhen] = useState('');
  const id = sermon.sermon.id;
  const act = useMutation({
    mutationFn: async (action: 'publish' | 'unpublish' | 'archive' | 'restore') => {
      const path = { params: { path: { id } } };
      switch (action) {
        case 'publish':
          return unwrap(await api.POST('/api/admin/media/sermons/{id}/publish', path));
        case 'unpublish':
          return unwrap(await api.POST('/api/admin/media/sermons/{id}/unpublish', path));
        case 'archive':
          return unwrap(await api.POST('/api/admin/media/sermons/{id}/archive', path));
        case 'restore':
          return unwrap(await api.POST('/api/admin/media/sermons/{id}/restore', path));
      }
    },
    onSuccess: onChange,
  });
  const schedule = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/admin/media/sermons/{id}/schedule', { params: { path: { id } }, body: { publishAt: new Date(when).toISOString() } })),
    onSuccess: onChange,
  });

  return (
    <Card title="Publishing">
      {sermon.publishProblems.length > 0 && (
        <ul className="list small">
          {sermon.publishProblems.map((p) => (
            <li key={p} className="list-row">
              {p}
            </li>
          ))}
        </ul>
      )}
      {sermon.status === 'Published' && <p className="small">Published {formatDateTime(sermon.sermon.publishedAt)}. Members can find it in the app and podcast.</p>}
      {sermon.status === 'Scheduled' && <p className="small">Will publish {formatDateTime(sermon.publishAt)}.</p>}

      {!canPublish ? (
        <p className="muted small">Someone with publishing rights will publish this sermon.</p>
      ) : (
        <div className="stack">
          {(sermon.status === 'Draft' || sermon.status === 'Scheduled') && (
            <>
              <Button variant="primary" busy={act.isPending} disabled={sermon.publishProblems.length > 0} onClick={() => act.mutate('publish')}>
                Publish now
              </Button>
              <div className="row">
                <TextInput type="datetime-local" value={when} onChange={(e) => setWhen(e.target.value)} />
                <Button busy={schedule.isPending} disabled={!when || sermon.publishProblems.length > 0} onClick={() => schedule.mutate()}>
                  Schedule
                </Button>
              </div>
            </>
          )}
          {(sermon.status === 'Published' || sermon.status === 'Scheduled') && (
            <Button onClick={() => act.mutate('unpublish')} busy={act.isPending}>
              Unpublish
            </Button>
          )}
          {sermon.status === 'Archived' ? (
            <Button onClick={() => act.mutate('restore')} busy={act.isPending}>
              Restore
            </Button>
          ) : (
            <Button variant="danger" onClick={() => act.mutate('archive')} busy={act.isPending}>
              Archive
            </Button>
          )}
        </div>
      )}
      <ErrorNote error={act.error ?? schedule.error} />
    </Card>
  );
}

function MediaCard({ sermon, kind, onChange }: { sermon: Admin; kind: Extract<MediaKind, 'Audio' | 'NotesPdf'>; onChange: (a: Admin) => void }) {
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  const id = sermon.sermon.id;
  const current = kind === 'Audio' ? sermon.sermon.audio : null;
  const pdfUrl = kind === 'NotesPdf' ? sermon.sermon.notesPdfUrl : null;

  const attach = async (assetId: string | null) => {
    const body = { assetId };
    const result =
      kind === 'Audio'
        ? unwrap(await api.PUT('/api/admin/media/sermons/{id}/audio', { params: { path: { id } }, body }))
        : unwrap(await api.PUT('/api/admin/media/sermons/{id}/notes-pdf', { params: { path: { id } }, body }));
    onChange(result);
  };

  const onFile = async (file: File | undefined) => {
    if (!file) return;
    setError(null);
    setProgress(0);
    try {
      const asset = await uploadFile(kind, file, setProgress);
      await attach(asset.id);
    } catch (err) {
      setError(err);
    } finally {
      setProgress(null);
    }
  };

  return (
    <Card title={kind === 'Audio' ? 'Audio' : 'Notes (PDF)'}>
      {kind === 'Audio' && current && (
        <div className="stack">
          <audio controls preload="none" src={current.url} />
          <span className="small muted">
            {formatDuration(current.durationSeconds)} · {formatBytes(current.sizeBytes)}
          </span>
        </div>
      )}
      {kind === 'Audio' && !current && (
        <p className="small muted">Upload an MP3 or M4A from the sound desk or recording. Around 64 kbps mono keeps it small for members on data.</p>
      )}
      {pdfUrl && (
        <a href={pdfUrl} target="_blank" rel="noreferrer">
          Open notes PDF
        </a>
      )}

      {progress !== null ? (
        <progress max={1} value={progress} />
      ) : (
        <div className="row">
          <label className="btn btn-secondary">
            {kind === 'Audio' ? (current ? 'Replace audio' : 'Upload audio') : pdfUrl ? 'Replace PDF' : 'Upload PDF'}
            <input
              type="file"
              hidden
              accept={kind === 'Audio' ? 'audio/mpeg,audio/mp4,audio/x-m4a,audio/aac,.mp3,.m4a' : 'application/pdf'}
              onChange={(e) => void onFile(e.target.files?.[0])}
            />
          </label>
          {(current || pdfUrl) && (
            <Button variant="ghost" onClick={() => void attach(null).catch(setError)}>
              Remove
            </Button>
          )}
        </div>
      )}
      <ErrorNote error={error} />
    </Card>
  );
}
