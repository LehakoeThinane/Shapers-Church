import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, TextInput } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';

type Message = Schemas['ModChatMessageDto'];
type Room = Schemas['ModeratorRoomDto'];

const holdLabel: Record<NonNullable<Schemas['ChatHoldReason']>, string> = {
  Approval: 'Waiting for approval',
  WordList: 'Matched the word list',
  Reports: 'Hidden after reports',
};

/** Services whose chat a moderator can look after: open now first, then recent and upcoming ones. */
export function ChatStreamsPage() {
  const streams = useQuery({ queryKey: ['chat', 'streams'], queryFn: async () => unwrap(await api.GET('/api/admin/media/chat')), refetchInterval: 30_000 });

  return (
    <>
      <PageHeader title="Live chat" subtitle="Moderate the chat during services. Every action is recorded in the audit log and can be undone." />
      <div className="grid-2">
        <Card title="Services">
          <ErrorNote error={streams.error} />
          {streams.isPending ? (
            <Loading />
          ) : streams.data?.length === 0 ? (
            <Empty>No services this week.</Empty>
          ) : (
            <ul className="list">
              {streams.data?.map((s) => (
                <li key={s.id} className="row">
                  <Link to={`/chat/${s.id}`}>
                    <strong>{s.title}</strong>
                  </Link>
                  <span className="small muted">{formatDateTime(s.scheduledStart)}</span>
                  {s.status === 'Live' && <Badge tone="accent">Live</Badge>}
                  {s.chatOpen ? <Badge tone="success">Chat open</Badge> : <Badge>Chat closed</Badge>}
                </li>
              ))}
            </ul>
          )}
        </Card>
        <WordList />
      </div>
    </>
  );
}

/** The moderator console for one service. Refreshes every few seconds while open. */
export function ChatConsolePage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const room = useQuery({
    queryKey: ['chat', 'room', id],
    queryFn: async () => unwrap(await api.GET('/api/admin/media/chat/{livestreamId}', { params: { path: { livestreamId: id } } })),
    refetchInterval: 3_000,
  });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['chat', 'room', id] });

  if (room.isPending) return <Loading />;
  if (!room.data) return <ErrorNote error={room.error} />;
  const data = room.data;
  const waiting = data.messages.filter((m) => m.status === 'Held');
  const reported = data.messages.filter((m) => m.status === 'Visible' && m.reports > 0);

  return (
    <>
      <PageHeader
        title={data.title}
        subtitle={
          <>
            <Link to="/chat">All services</Link> · {data.rules.open ? 'Chat open' : 'Chat closed'}
          </>
        }
      />
      <ErrorNote error={room.error} />
      <div className="grid-2">
        <div className="stack">
          <Card title={`Needs a look (${waiting.length + reported.length})`}>
            {waiting.length + reported.length === 0 ? (
              <Empty>Nothing waiting.</Empty>
            ) : (
              <ul className="list">
                {[...waiting, ...reported].map((m) => (
                  <MessageItem key={m.id} room={data} message={m} onDone={refresh} />
                ))}
              </ul>
            )}
          </Card>
          <Rules room={data} onDone={refresh} />
          <Sanctions room={data} onDone={refresh} />
        </div>
        <Card title="Everything said">
          <p className="small muted">Newest at the bottom. Hidden messages are struck through and only visible here.</p>
          {data.messages.length === 0 ? (
            <Empty>No messages yet.</Empty>
          ) : (
            <ul className="list chat-log">
              {data.messages.map((m) => (
                <MessageItem key={m.id} room={data} message={m} onDone={refresh} compact />
              ))}
            </ul>
          )}
          <Post livestreamId={data.livestreamId} onDone={refresh} />
        </Card>
      </div>
    </>
  );
}

function MessageItem({ room, message: m, onDone, compact = false }: { room: Room; message: Message; onDone: () => void; compact?: boolean }) {
  const path = { params: { path: { livestreamId: room.livestreamId, messageId: m.id } } };
  const hide = useMutation({ mutationFn: async () => unwrap(await api.POST('/api/admin/media/chat/{livestreamId}/messages/{messageId}/hide', path)), onSuccess: onDone });
  const show = useMutation({ mutationFn: async () => unwrap(await api.POST('/api/admin/media/chat/{livestreamId}/messages/{messageId}/show', path)), onSuccess: onDone });
  const sanction = useMutation({
    mutationFn: async (kind: Schemas['ChatSanctionKind']) => {
      const reason = prompt(kind === 'Ban' ? 'Why are you banning them? (Kept with the record.)' : 'Why are you timing them out? (Optional.)');
      if (reason === null) return;
      return unwrap(
        await api.POST('/api/admin/media/chat/{livestreamId}/sanctions', {
          params: { path: { livestreamId: room.livestreamId } },
          body: { personId: m.personId, kind, reason: reason || null, hideMessages: confirm('Also hide everything they said in this service?') },
        }),
      );
    },
    onSuccess: onDone,
  });

  return (
    <li className="stack review-item">
      <div className="row">
        <strong>{m.author}</strong>
        {m.fromTeam && <Badge tone="accent">Team</Badge>}
        <span className="small muted">{formatDateTime(m.sentAt)}</span>
        {m.status === 'Held' && m.holdReason && <Badge tone="accent">{holdLabel[m.holdReason]}</Badge>}
        {m.status === 'Hidden' && <Badge tone="danger">Hidden</Badge>}
        {m.reports > 0 && <Badge tone="danger">{m.reports === 1 ? '1 report' : `${m.reports} reports`}</Badge>}
      </div>
      <p style={m.status === 'Hidden' ? { textDecoration: 'line-through', opacity: 0.6 } : undefined}>{m.text}</p>
      <ErrorNote error={hide.error ?? show.error ?? sanction.error} />
      <div className="row">
        {m.status !== 'Hidden' && (
          <Button variant={compact ? 'ghost' : 'danger'} busy={hide.isPending} onClick={() => hide.mutate()}>
            Hide
          </Button>
        )}
        {m.status !== 'Visible' && (
          <Button variant={compact ? 'ghost' : 'primary'} busy={show.isPending} onClick={() => show.mutate()}>
            {m.status === 'Held' ? 'Approve' : 'Show again'}
          </Button>
        )}
        {!m.fromTeam && !compact && (
          <>
            <Button variant="ghost" busy={sanction.isPending} onClick={() => sanction.mutate('Timeout')}>
              Time out for this service
            </Button>
            <Button variant="ghost" busy={sanction.isPending} onClick={() => sanction.mutate('Ban')}>
              Ban from chat
            </Button>
          </>
        )}
      </div>
    </li>
  );
}

function Rules({ room, onDone }: { room: Room; onDone: () => void }) {
  const [draft, setDraft] = useState<string | null>(null);
  const slow = draft ?? String(room.rules.slowSeconds);
  const save = useMutation({
    mutationFn: async (approvalRequired: boolean) =>
      unwrap(
        await api.PUT('/api/admin/media/chat/{livestreamId}/rules', {
          params: { path: { livestreamId: room.livestreamId } },
          body: { slowSeconds: Number(slow), approvalRequired },
        }),
      ),
    onSuccess: () => {
      setDraft(null);
      onDone();
    },
  });

  return (
    <Card title="Chat rules">
      <div className="stack">
        <Field label="Slow mode (seconds between messages)" hint="5 to 300. Moderators aren't slowed down.">
          <TextInput type="number" min={5} max={300} value={slow} onChange={(e) => setDraft(e.target.value)} />
        </Field>
        <ErrorNote error={save.error} />
        <div className="row">
          <Button busy={save.isPending} onClick={() => save.mutate(room.rules.approvalRequired)}>
            Save slow mode
          </Button>
          <Button variant={room.rules.approvalRequired ? 'primary' : 'secondary'} busy={save.isPending} onClick={() => save.mutate(!room.rules.approvalRequired)}>
            {room.rules.approvalRequired ? 'Stop approving every message' : 'Approve every message'}
          </Button>
        </div>
      </div>
    </Card>
  );
}

function Sanctions({ room, onDone }: { room: Room; onDone: () => void }) {
  const lift = useMutation({
    mutationFn: async (sanctionId: string) => unwrap(await api.POST('/api/admin/media/chat/sanctions/{sanctionId}/lift', { params: { path: { sanctionId } } })),
    onSuccess: onDone,
  });

  return (
    <Card title="Timed out and banned">
      <ErrorNote error={lift.error} />
      {room.sanctions.length === 0 ? (
        <Empty>Nobody.</Empty>
      ) : (
        <ul className="list">
          {room.sanctions.map((s) => (
            <li key={s.id} className="row">
              <strong>{s.name}</strong>
              <Badge tone={s.kind === 'Ban' ? 'danger' : 'accent'}>{s.kind === 'Ban' ? 'Banned' : 'Timed out'}</Badge>
              {s.reason && <span className="small muted">{s.reason}</span>}
              <Button variant="ghost" busy={lift.isPending} onClick={() => lift.mutate(s.id)}>
                Lift
              </Button>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function Post({ livestreamId, onDone }: { livestreamId: string; onDone: () => void }) {
  const [text, setText] = useState('');
  const post = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/media/live/{livestreamId}/chat', { params: { path: { livestreamId } }, body: { text } })),
    onSuccess: () => {
      setText('');
      onDone();
    },
  });

  return (
    <form
      className="stack"
      onSubmit={(e) => {
        e.preventDefault();
        if (text.trim()) post.mutate();
      }}>
      <Field label="Post as the team" hint="Shown with a Team badge, e.g. a welcome or where to find the notes.">
        <TextInput maxLength={300} value={text} onChange={(e) => setText(e.target.value)} />
      </Field>
      <ErrorNote error={post.error} />
      <div className="row">
        <Button variant="primary" type="submit" busy={post.isPending} disabled={!text.trim()}>
          Post
        </Button>
      </div>
    </form>
  );
}

function WordList() {
  const queryClient = useQueryClient();
  const words = useQuery({ queryKey: ['chat', 'words'], queryFn: async () => unwrap(await api.GET('/api/admin/media/chat/words')) });
  const [draft, setDraft] = useState<string | null>(null);
  const value = draft ?? words.data?.terms.join('\n') ?? '';
  const save = useMutation({
    mutationFn: async () =>
      unwrap(await api.PUT('/api/admin/media/chat/words', { body: { terms: value.split('\n').map((t) => t.trim()).filter(Boolean) } })),
    onSuccess: () => {
      setDraft(null);
      void queryClient.invalidateQueries({ queryKey: ['chat', 'words'] });
    },
  });

  return (
    <Card title="Word list">
      <div className="stack">
        <p className="small muted">
          Messages containing any of these words or phrases wait for a moderator instead of appearing. One per line; whole words, any capitals. Changing it needs
          church-wide moderation rights.
        </p>
        {words.isPending ? (
          <Loading />
        ) : (
          <textarea className="input" rows={8} value={value} onChange={(e) => setDraft(e.target.value)} aria-label="Blocked words and phrases" />
        )}
        <ErrorNote error={words.error ?? save.error} />
        <div className="row">
          <Button variant="primary" busy={save.isPending} disabled={draft === null} onClick={() => save.mutate()}>
            Save word list
          </Button>
        </div>
      </div>
    </Card>
  );
}
