import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, TextInput } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';

type Stream = Schemas['LivestreamAdminDto'];

const toLocalInput = (iso: string) => {
  const d = new Date(iso);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
};

/** Sunday 09:00 local time, the usual service start, as the default for a new stream. */
function nextSunday(): string {
  const d = new Date();
  d.setDate(d.getDate() + ((7 - d.getDay()) % 7 || 7));
  d.setHours(9, 0, 0, 0);
  return toLocalInput(d.toISOString());
}

const statusTone = { Scheduled: 'neutral', Live: 'accent', Ended: 'success', Cancelled: 'danger' } as const;

export function LivestreamsPage() {
  const navigate = useNavigate();
  const streams = useQuery({ queryKey: ['livestreams'], queryFn: async () => unwrap(await api.GET('/api/admin/media/livestreams')) });
  const [form, setForm] = useState({ title: 'Sunday service', start: nextSunday(), videoUrl: '' });

  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/media/livestreams', {
          body: { title: form.title, scheduledStart: new Date(form.start).toISOString(), videoUrl: form.videoUrl || null, notes: null, giveUrl: null, scope: null },
        }),
      ),
    onSuccess: (s) => navigate(`/livestreams/${s.id}`),
  });

  return (
    <>
      <PageHeader title="Livestream" subtitle="Schedule the service, go live from the console, and put scripture on members' screens." />
      <div className="grid-2">
        <Card title="Streams">
          <ErrorNote error={streams.error} />
          {streams.isPending ? (
            <Loading />
          ) : streams.data?.length === 0 ? (
            <Empty>No streams yet.</Empty>
          ) : (
            <ul className="list">
              {streams.data?.map((s) => (
                <li key={s.id} className="list-row">
                  <Link to={`/livestreams/${s.id}`}>{s.title}</Link>
                  <span className="row">
                    <span className="small muted">{formatDateTime(s.scheduledStart)}</span>
                    <Badge tone={statusTone[s.status]}>{s.status}</Badge>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card title="Schedule a stream">
          <form
            className="stack"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              create.mutate();
            }}>
            <Field label="Title">
              <TextInput required maxLength={150} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
            </Field>
            <Field label="Starts">
              <TextInput type="datetime-local" required value={form.start} onChange={(e) => setForm({ ...form, start: e.target.value })} />
            </Field>
            <Field label="YouTube Live link" hint="From YouTube Studio → Go live → Share. You can add it later.">
              <TextInput placeholder="https://www.youtube.com/live/…" value={form.videoUrl} onChange={(e) => setForm({ ...form, videoUrl: e.target.value })} />
            </Field>
            <ErrorNote error={create.error} />
            <Button variant="primary" type="submit" busy={create.isPending}>
              Schedule
            </Button>
          </form>
        </Card>
      </div>
    </>
  );
}

export function LivestreamConsolePage() {
  const { id = '' } = useParams();
  const stream = useQuery({
    queryKey: ['livestream', id],
    queryFn: async () => unwrap(await api.GET('/api/admin/media/livestreams/{id}', { params: { path: { id } } })),
  });

  if (stream.isPending) return <Loading />;
  if (stream.error || !stream.data) return <ErrorNote error={stream.error ?? new Error('Not found')} />;
  return <Console key={stream.data.id} stream={stream.data} />;
}

function Console({ stream }: { stream: Stream }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const saved = (s: Stream) => {
    queryClient.setQueryData(['livestream', s.id], s);
    void queryClient.invalidateQueries({ queryKey: ['livestreams'] });
  };
  const path = { params: { path: { id: stream.id } } };

  const control = useMutation({
    mutationFn: async (action: 'go-live' | 'end' | 'cancel') => {
      if (action === 'go-live') return unwrap(await api.POST('/api/admin/media/livestreams/{id}/go-live', path));
      if (action === 'end') return unwrap(await api.POST('/api/admin/media/livestreams/{id}/end', path));
      return unwrap(await api.POST('/api/admin/media/livestreams/{id}/cancel', path));
    },
    onSuccess: saved,
  });
  const show = useMutation({
    mutationFn: async (cueId: string | null) => unwrap(await api.POST('/api/admin/media/livestreams/{id}/on-screen', { ...path, body: { cueId } })),
    onSuccess: saved,
  });
  const makeSermon = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/media/livestreams/{id}/make-sermon', path)),
    onSuccess: (sermonId) => navigate(`/sermons/${sermonId}`),
  });

  const finished = stream.status === 'Ended' || stream.status === 'Cancelled';
  const live = stream.status === 'Live';

  return (
    <>
      <PageHeader
        title={stream.title}
        subtitle={
          <>
            <Link to="/livestreams">All streams</Link> · {formatDateTime(stream.scheduledStart)}
          </>
        }
        actions={<Badge tone={statusTone[stream.status]}>{live ? '● LIVE' : stream.status}</Badge>}
      />

      <div className="grid-2">
        <div className="stack">
          <Card title="Control">
            {stream.status === 'Scheduled' && (
              <div className="stack">
                <p className="small muted">Start the stream in YouTube first, then go live here so the app shows it.</p>
                <Button variant="primary" busy={control.isPending} disabled={!stream.video} onClick={() => control.mutate('go-live')}>
                  Go live
                </Button>
                {!stream.video && <p className="small">Add the YouTube Live link below first.</p>}
                <Button variant="ghost" onClick={() => control.mutate('cancel')}>
                  Cancel this stream
                </Button>
              </div>
            )}
            {live && (
              <div className="stack">
                <p className="small">Live since {formatDateTime(stream.startedAt)}.</p>
                <Button variant="danger" busy={control.isPending} onClick={() => confirm('End the stream for everyone?') && control.mutate('end')}>
                  End stream
                </Button>
              </div>
            )}
            {stream.status === 'Ended' && (
              <div className="stack">
                <p className="small">Ended {formatDateTime(stream.endedAt)}.</p>
                {stream.sermonId ? (
                  <Link className="btn btn-secondary" to={`/sermons/${stream.sermonId}`}>
                    Open the sermon
                  </Link>
                ) : can(access, Permissions.mediaEdit) ? (
                  <Button variant="primary" busy={makeSermon.isPending} onClick={() => makeSermon.mutate()}>
                    Make this a sermon
                  </Button>
                ) : null}
              </div>
            )}
            <ErrorNote error={control.error ?? makeSermon.error} />
          </Card>

          <ScriptureCard stream={stream} onChange={saved} show={(cueId) => show.mutate(cueId)} showError={show.error} />
        </div>

        {!finished && <DetailsCard stream={stream} onChange={saved} />}
      </div>
    </>
  );
}

function ScriptureCard({ stream, onChange, show, showError }: { stream: Stream; onChange: (s: Stream) => void; show: (cueId: string | null) => void; showError: unknown }) {
  const [reference, setReference] = useState('');
  const [text, setText] = useState('');
  const path = { params: { path: { id: stream.id } } };
  const add = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/media/livestreams/{id}/cues', { ...path, body: { reference, text: text || null } })),
    onSuccess: (s) => {
      setReference('');
      setText('');
      onChange(s);
    },
  });
  const remove = useMutation({
    mutationFn: async (cueId: string) => unwrap(await api.DELETE('/api/admin/media/livestreams/{id}/cues/{cueId}', { params: { path: { id: stream.id, cueId } } })),
    onSuccess: onChange,
  });
  const live = stream.status === 'Live';
  const finished = stream.status === 'Ended' || stream.status === 'Cancelled';

  return (
    <Card title="Scripture on screen" actions={live && stream.currentCueId && <Button onClick={() => show(null)}>Clear screen</Button>}>
      {stream.cues.length === 0 && <Empty>Add the passages for this service in the order they'll be read.</Empty>}
      <ul className="list">
        {stream.cues.map((c) => {
          const onScreen = c.id === stream.currentCueId;
          return (
            <li key={c.id} className="list-row">
              <span>
                <strong>{c.reference}</strong> {onScreen && <Badge tone="accent">On screen</Badge>}
                {c.text && <span className="small muted block">{c.text.length > 90 ? `${c.text.slice(0, 90)}…` : c.text}</span>}
              </span>
              <span className="row">
                {live && !onScreen && (
                  <Button variant="primary" onClick={() => show(c.id)}>
                    Show
                  </Button>
                )}
                {!finished && (
                  <Button variant="ghost" onClick={() => remove.mutate(c.id)}>
                    Remove
                  </Button>
                )}
              </span>
            </li>
          );
        })}
      </ul>
      {!finished && (
        <form
          className="stack"
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            add.mutate();
          }}>
          <Field label="Passage">
            <TextInput required placeholder="Isaiah 42:1-4" value={reference} onChange={(e) => setReference(e.target.value)} />
          </Field>
          <Field label="Verse text (optional)" hint="Use the World English Bible (public domain) until a licensed translation is agreed.">
            <textarea className="input" rows={3} value={text} onChange={(e) => setText(e.target.value)} />
          </Field>
          <Button type="submit" busy={add.isPending}>
            Add passage
          </Button>
        </form>
      )}
      <ErrorNote error={add.error ?? remove.error ?? showError} />
    </Card>
  );
}

function DetailsCard({ stream, onChange }: { stream: Stream; onChange: (s: Stream) => void }) {
  const [form, setForm] = useState({
    title: stream.title,
    start: toLocalInput(stream.scheduledStart),
    videoUrl: stream.video?.watchUrl ?? '',
    notes: stream.notes ?? '',
    giveUrl: stream.giveUrl ?? '',
  });
  const save = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT('/api/admin/media/livestreams/{id}', {
          params: { path: { id: stream.id } },
          body: {
            title: form.title,
            scheduledStart: new Date(form.start).toISOString(),
            videoUrl: form.videoUrl || null,
            notes: form.notes || null,
            giveUrl: form.giveUrl || null,
            scope: null,
          },
        }),
      ),
    onSuccess: onChange,
  });

  return (
    <Card title="Details">
      <form
        className="stack"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          save.mutate();
        }}>
        <Field label="Title">
          <TextInput required value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
        </Field>
        <Field label="Starts">
          <TextInput type="datetime-local" required value={form.start} onChange={(e) => setForm({ ...form, start: e.target.value })} />
        </Field>
        <Field label="YouTube Live link">
          <TextInput value={form.videoUrl} onChange={(e) => setForm({ ...form, videoUrl: e.target.value })} />
        </Field>
        <Field label="Give link" hint="Until in-app giving arrives, e.g. the church's Yoco payment page.">
          <TextInput placeholder="https://pay.yoco.com/shapers-church" value={form.giveUrl} onChange={(e) => setForm({ ...form, giveUrl: e.target.value })} />
        </Field>
        <Field label="Notes for the Notes tab (Markdown)">
          <textarea className="input" rows={10} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
        </Field>
        <ErrorNote error={save.error} />
        <Button variant="primary" type="submit" busy={save.isPending}>
          Save details
        </Button>
      </form>
    </Card>
  );
}
