import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, covers, Permissions, scopesFor, useAccess } from '../lib/access';
import { RewriteHelp } from '../components/Assist';
import { useScopes } from '../lib/queries';
import { scopeLabel, useScope } from '../lib/scope-context';

type Announcement = Schemas['AnnouncementDto'];
type Status = Schemas['AnnouncementStatus'];

const statusLabel: Record<Status, string> = {
  Draft: 'Draft',
  AwaitingApproval: 'Waiting for approval',
  Queued: 'Ready to send',
  Sent: 'Sent',
  Cancelled: 'Cancelled',
};
const statusTone = { Draft: 'neutral', AwaitingApproval: 'accent', Queued: 'accent', Sent: 'success', Cancelled: 'danger' } as const;

const toLocalInput = (iso: string | null | undefined) => {
  if (!iso) return '';
  const d = new Date(iso);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
};

export function AnnouncementsPage() {
  const { data: access } = useAccess();
  const { nameOf } = useScope();
  const list = useQuery({ queryKey: ['announcements'], queryFn: async () => unwrap(await api.GET('/api/admin/announcements')) });
  const waiting = list.data?.filter((a) => a.status === 'AwaitingApproval' && !a.createdByMe) ?? [];

  return (
    <>
      <PageHeader
        title="Announcements"
        subtitle="News for the church family: in the app inbox, by push notification, and by email to people who asked for it."
        actions={
          can(access, Permissions.announcementsSend) && (
            <Link className="btn btn-primary" to="/announcements/new">
              New announcement
            </Link>
          )
        }
      />
      {waiting.length > 0 && can(access, Permissions.announcementsApprove) && (
        <Card title="Waiting for your approval">
          <ul className="list">
            {waiting.map((a) => (
              <li key={a.id} className="list-row">
                <Link to={`/announcements/${a.id}`}>{a.title}</Link>
                <span className="small muted">{nameOf(a.scope)}</span>
              </li>
            ))}
          </ul>
        </Card>
      )}
      <Card>
        <ErrorNote error={list.error} />
        {list.isPending ? (
          <Loading />
        ) : list.data?.length === 0 ? (
          <Empty>No announcements yet.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Announcement</th>
                <th>To</th>
                <th>When</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {list.data?.map((a) => (
                <tr key={a.id}>
                  <td>
                    <Link to={`/announcements/${a.id}`}>{a.title}</Link>
                  </td>
                  <td>{nameOf(a.scope)}</td>
                  <td>{a.sentAt ? formatDateTime(a.sentAt) : a.sendAt ? `From ${formatDateTime(a.sendAt)}` : 'As soon as ready'}</td>
                  <td>
                    <Badge tone={statusTone[a.status]}>{statusLabel[a.status]}</Badge>
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

export function AnnouncementEditorPage() {
  const { id } = useParams();
  const announcement = useQuery({
    queryKey: ['announcement', id],
    enabled: !!id,
    queryFn: async () => unwrap(await api.GET('/api/admin/announcements/{id}', { params: { path: { id: id! } } })),
  });

  if (!id) return <Editor key="new" />;
  if (announcement.isPending) return <Loading />;
  if (announcement.error || !announcement.data) return <ErrorNote error={announcement.error ?? new Error('Not found')} />;
  return <Editor key={`${announcement.data.id}-${announcement.data.updatedAt}`} existing={announcement.data} />;
}

function Editor({ existing }: { existing?: Announcement }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const { nameOf } = useScope();
  const { data: scopes = [] } = useScopes();
  const sendScopes = scopesFor(access, Permissions.announcementsSend);
  const allowedScopes = scopes.filter((s) => sendScopes.some((m) => covers(m, s.path)));

  const [form, setForm] = useState({
    title: existing?.title ?? '',
    body: existing?.body ?? '',
    link: existing?.link ?? '',
    scope: existing?.scope ?? allowedScopes[0]?.path ?? '',
    sendEmail: existing?.sendEmail ?? true,
    sendAt: toLocalInput(existing?.sendAt),
  });
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));
  const saved = (a: Announcement) => {
    queryClient.setQueryData(['announcement', a.id], a);
    void queryClient.invalidateQueries({ queryKey: ['announcements'] });
  };

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        title: form.title,
        body: form.body,
        link: form.link || null,
        scope: form.scope,
        sendEmail: form.sendEmail,
        sendAt: form.sendAt ? new Date(form.sendAt).toISOString() : null,
      };
      return existing
        ? unwrap(await api.PUT('/api/admin/announcements/{id}', { params: { path: { id: existing.id } }, body }))
        : unwrap(await api.POST('/api/admin/announcements', { body }));
    },
    onSuccess: (a) => {
      saved(a);
      if (!existing) navigate(`/announcements/${a.id}`, { replace: true });
    },
  });

  const action = useMutation({
    mutationFn: async ({ kind, note }: { kind: 'submit' | 'approve' | 'return' | 'cancel'; note?: string }) => {
      const path = { params: { path: { id: existing!.id } } };
      if (kind === 'submit') return unwrap(await api.POST('/api/admin/announcements/{id}/submit', path));
      if (kind === 'approve') return unwrap(await api.POST('/api/admin/announcements/{id}/approve', path));
      if (kind === 'return') return unwrap(await api.POST('/api/admin/announcements/{id}/return', { ...path, body: { note: note ?? null } }));
      return unwrap(await api.POST('/api/admin/announcements/{id}/cancel', path));
    },
    onSuccess: saved,
  });

  const draft = !existing || existing.status === 'Draft';
  const canApprove = existing?.status === 'AwaitingApproval' && !existing.createdByMe && can(access, Permissions.announcementsApprove);
  const wide = scopes.find((s) => s.path === form.scope)?.type;
  const needsApproval = existing?.needsApproval ?? (wide === 'Global' || wide === 'Campus');

  return (
    <>
      <PageHeader
        title={existing ? existing.title : 'New announcement'}
        subtitle={<Link to="/announcements">All announcements</Link>}
        actions={existing && <Badge tone={statusTone[existing.status]}>{statusLabel[existing.status]}</Badge>}
      />
      <div className="grid-2">
        <Card title="Message">
          <form
            className="stack"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              save.mutate();
            }}>
            <fieldset className="stack" disabled={!draft || !can(access, Permissions.announcementsSend)}>
              <Field label="Title" hint="Shown in bold on phones and as the email subject.">
                <TextInput required maxLength={120} value={form.title} onChange={(e) => set({ title: e.target.value })} />
              </Field>
              <Field label="Message" hint="Phones show the first two lines; the inbox and email show it all.">
                <textarea className="input" rows={7} required maxLength={2000} value={form.body} onChange={(e) => set({ body: e.target.value })} />
              </Field>
              <RewriteHelp text={form.body} onApply={(body) => set({ body })} />
              <Field label="Open in the app (optional)" hint="A place in the app, e.g. /events or /prayer.">
                <TextInput maxLength={300} value={form.link} placeholder="/events" onChange={(e) => set({ link: e.target.value })} />
              </Field>
              <Field label="Send to" hint="Ministries reach people whose record is in that ministry. Group lists arrive with Groups.">
                <Select required value={form.scope} onChange={(e) => set({ scope: e.target.value })}>
                  {allowedScopes.map((s) => (
                    <option key={s.path} value={s.path}>
                      {scopeLabel(s)}
                    </option>
                  ))}
                </Select>
              </Field>
              <label className="checkbox">
                <input type="checkbox" checked={form.sendEmail} onChange={(e) => set({ sendEmail: e.target.checked })} />
                Also email people who agreed to church emails
              </label>
              <Field label="Send at (optional)" hint="Empty: as soon as it's ready. Nothing buzzes between 21:00 and 07:00.">
                <TextInput type="datetime-local" value={form.sendAt} onChange={(e) => set({ sendAt: e.target.value })} />
              </Field>
            </fieldset>
            <ErrorNote error={save.error} />
            {draft && can(access, Permissions.announcementsSend) && (
              <Button variant="primary" type="submit" busy={save.isPending}>
                {existing ? 'Save draft' : 'Create draft'}
              </Button>
            )}
          </form>
        </Card>

        <div className="stack">
          {existing?.returnNote && existing.status === 'Draft' && (
            <Card title="Sent back">
              <p>{existing.returnNote}</p>
            </Card>
          )}
          {existing?.audience && (
            <Card title="Who it will reach">
              <p>
                <strong>{existing.audience.people}</strong> people in {nameOf(existing.scope)}: in the app inbox for those with the app (
                {existing.audience.withApp}), by push if they allow it, and by email to {existing.audience.byEmail}.
              </p>
            </Card>
          )}
          {existing?.delivered && (
            <Card title="Delivered">
              <p>
                {existing.delivered.inbox} inboxes · {existing.delivered.pushed} pushed · {existing.delivered.emailed} emailed
                {existing.delivered.failed > 0 && ` · ${existing.delivered.failed} failed`}
              </p>
              <p className="small muted">Sent {formatDateTime(existing.sentAt)}. Pushes wait until 07:00 if sent late at night.</p>
            </Card>
          )}
          {existing && (
            <Card title="Next step">
              <div className="stack">
                <ErrorNote error={action.error} />
                {existing.status === 'Draft' && can(access, Permissions.announcementsSend) && (
                  <>
                    <p className="small muted">
                      {needsApproval
                        ? 'This goes to a whole campus or the whole church, so someone else must approve it before it is sent.'
                        : 'This goes to a ministry, so it is sent as soon as you submit it (or at the time you chose).'}
                    </p>
                    <Button variant="primary" busy={action.isPending} onClick={() => action.mutate({ kind: 'submit' })}>
                      {needsApproval ? 'Submit for approval' : 'Send'}
                    </Button>
                  </>
                )}
                {existing.status === 'AwaitingApproval' && existing.createdByMe && <p className="small muted">Waiting for someone else to approve it.</p>}
                {canApprove && <ApproveActions busy={action.isPending} onApprove={() => action.mutate({ kind: 'approve' })} onReturn={(note) => action.mutate({ kind: 'return', note })} />}
                {(existing.status === 'AwaitingApproval' || existing.status === 'Queued') && existing.createdByMe && (
                  <Button busy={action.isPending} onClick={() => action.mutate({ kind: 'return' })}>
                    Back to draft
                  </Button>
                )}
                {existing.status !== 'Sent' && existing.status !== 'Cancelled' && can(access, Permissions.announcementsSend) && (
                  <Button variant="danger" busy={action.isPending} onClick={() => confirm('Cancel this announcement?') && action.mutate({ kind: 'cancel' })}>
                    Cancel announcement
                  </Button>
                )}
              </div>
            </Card>
          )}
        </div>
      </div>
    </>
  );
}

function ApproveActions({ busy, onApprove, onReturn }: { busy: boolean; onApprove: () => void; onReturn: (note: string) => void }) {
  const [note, setNote] = useState('');
  return (
    <div className="stack">
      <Button variant="primary" busy={busy} onClick={() => confirm('Approve and send this announcement?') && onApprove()}>
        Approve
      </Button>
      <Field label="Or send it back with a note">
        <textarea className="input" rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      <Button busy={busy} disabled={!note.trim()} onClick={() => onReturn(note)}>
        Send back to the author
      </Button>
    </div>
  );
}
