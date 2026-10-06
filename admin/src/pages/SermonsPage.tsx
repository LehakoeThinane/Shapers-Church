import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDate, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';

type Status = Schemas['SermonStatus'];

const statusTone: Record<Status, 'neutral' | 'accent' | 'success' | 'danger'> = {
  Draft: 'neutral',
  Scheduled: 'accent',
  Published: 'success',
  Archived: 'danger',
};

/**
 * Captions as transcripts: the channel's owner signs in with Google once (in a new tab), then sermons with a YouTube
 * video get their captions as a transcript every hour.
 */
function YouTubeCaptionsCard() {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const [connecting, setConnecting] = useState(false);
  const status = useQuery({
    queryKey: ['youtube-connection'],
    queryFn: async () => unwrap(await api.GET('/api/admin/media/youtube')),
    // After "Connect", check back while the owner signs in in the other tab.
    refetchInterval: (q) => (q.state.data && !q.state.data.connected && connecting ? 5000 : false),
  });
  const connect = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/media/youtube/connect')),
    onSuccess: (start) => {
      setConnecting(true);
      window.open(start.authorizeUrl, '_blank', 'noopener');
    },
  });
  const disconnect = useMutation({
    mutationFn: async () => unwrap(await api.DELETE('/api/admin/media/youtube')),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['youtube-connection'] }),
  });

  const s = status.data;
  if (!s || !s.configured) return null;
  const canConnect = can(access, Permissions.mediaPublish);
  return (
    <Card title="Transcripts from YouTube captions">
      {s.connected ? (
        <div className="row">
          <span>
            Connected to <strong>{s.channelTitle}</strong> {!s.isChurchChannel && <Badge tone="danger">Not the church's usual channel</Badge>}
            <span className="small muted">
              {' '}
              · {s.sermonsWaiting > 0 ? `${s.sermonsWaiting} sermons waiting for captions; a few are fetched every hour.` : 'Every sermon with a video has been checked.'}
            </span>
          </span>
          {canConnect && (
            <Button variant="ghost" busy={disconnect.isPending} onClick={() => window.confirm('Stop reading captions from YouTube?') && disconnect.mutate()}>
              Disconnect
            </Button>
          )}
        </div>
      ) : (
        <div className="stack-tight">
          <p className="small muted">
            Sermon videos' captions can become transcripts, so AI help can draft notes and cell lessons. YouTube only shares captions with the channel's owner: the person
            who owns the church's YouTube channel signs in with Google once.
          </p>
          {canConnect ? (
            <span>
              <Button busy={connect.isPending} onClick={() => connect.mutate()}>
                Connect the church's channel
              </Button>
              {connecting && <span className="small muted"> Finish signing in on the Google tab. This updates by itself.</span>}
            </span>
          ) : (
            <p className="small muted">Someone who publishes sermons can connect it.</p>
          )}
        </div>
      )}
      <ErrorNote error={status.error ?? connect.error ?? disconnect.error} />
    </Card>
  );
}

export function SermonsPage() {
  const queryClient = useQueryClient();
  const [q, setQ] = useState('');
  const [status, setStatus] = useState<Status | ''>('');
  const [page, setPage] = useState(1);

  const sermons = useQuery({
    queryKey: ['admin-sermons', q, status, page],
    queryFn: async () =>
      unwrap(await api.GET('/api/admin/media/sermons', { params: { query: { Q: q || undefined, Status: status || undefined, Page: page, PageSize: 25 } } })),
    placeholderData: keepPreviousData,
  });

  const importFromYouTube = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/media/import/youtube')),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-sermons'] }),
  });

  const pages = sermons.data ? Math.max(1, Math.ceil(sermons.data.total / 25)) : 1;

  return (
    <>
      <PageHeader
        title="Sermons"
        subtitle="Audio first: every sermon should have an audio file so members can listen on small data bundles."
        actions={
          <>
            <Link className="btn btn-secondary" to="/sermons/library">
              Series & speakers
            </Link>
            <Button busy={importFromYouTube.isPending} onClick={() => importFromYouTube.mutate()}>
              Import from YouTube
            </Button>
            <Link className="btn btn-primary" to="/sermons/new">
              New sermon
            </Link>
          </>
        }
      />

      {importFromYouTube.data && (
        <p className="note note-accent">
          Found {importFromYouTube.data.found} videos. {importFromYouTube.data.created} new drafts created
          {importFromYouTube.data.alreadyImported > 0 && `, ${importFromYouTube.data.alreadyImported} already imported`}. Add speakers and series, then publish.
        </p>
      )}
      <ErrorNote error={importFromYouTube.error} />
      <YouTubeCaptionsCard />

      <Card>
        <div className="filters">
          <Field label="Search">
            <TextInput
              placeholder="Title, speaker, scripture"
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setPage(1);
              }}
            />
          </Field>
          <Field label="Status">
            <Select
              value={status}
              onChange={(e) => {
                setStatus(e.target.value as Status | '');
                setPage(1);
              }}>
              <option value="">Any</option>
              <option value="Draft">Draft</option>
              <option value="Scheduled">Scheduled</option>
              <option value="Published">Published</option>
              <option value="Archived">Archived</option>
            </Select>
          </Field>
        </div>

        <ErrorNote error={sermons.error} />
        {sermons.isPending ? (
          <Loading />
        ) : sermons.data?.items.length === 0 ? (
          <Empty>No sermons yet. Create one, or import the church's YouTube videos as drafts.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Preached</th>
                <th>Speakers</th>
                <th>Series</th>
                <th>Media</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {sermons.data?.items.map((s) => (
                <tr key={s.id}>
                  <td>
                    <Link to={`/sermons/${s.id}`}>{s.title}</Link>
                  </td>
                  <td className="nowrap">{formatDate(s.preachedOn)}</td>
                  <td>{s.speakers.join(', ') || <span className="muted">none yet</span>}</td>
                  <td>{s.seriesTitle ?? '—'}</td>
                  <td className="nowrap">
                    {s.hasAudio ? 'Audio' : <span className="muted">no audio</span>}
                    {s.hasVideo ? ' · Video' : ''}
                  </td>
                  <td>
                    <Badge tone={statusTone[s.status]}>{s.status}</Badge>
                    {s.status === 'Scheduled' && s.publishAt && <span className="small muted"> {formatDate(s.publishAt)}</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {pages > 1 && (
          <div className="pager">
            <Button disabled={page <= 1} onClick={() => setPage(page - 1)}>
              Previous
            </Button>
            <span className="muted small">
              Page {page} of {pages}
            </span>
            <Button disabled={page >= pages} onClick={() => setPage(page + 1)}>
              Next
            </Button>
          </div>
        )}
      </Card>
    </>
  );
}

