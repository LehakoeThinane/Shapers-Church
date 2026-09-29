import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';

type Status = Schemas['ConnectCardStatus'];
type Reason = Schemas['ConnectReason'];

const reasonLabels: Record<Reason, string> = {
  FirstTime: 'First time',
  Decision: 'Made a decision',
  Prayer: 'Prayer',
  MoreInfo: 'Wants to know more',
  JoinGroup: 'Join a group',
  Serve: 'Wants to serve',
};

/** People who raised a hand during a service or online. Someone from the campus team follows each one up. */
export function ConnectCardsPage() {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<Status | ''>('New');
  const cards = useQuery({
    queryKey: ['connect-cards', status],
    queryFn: async () => unwrap(await api.GET('/api/admin/connect-cards', { params: { query: { status: status || undefined } } })),
  });
  const [notes, setNotes] = useState<Record<string, string>>({});
  const handle = useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/admin/connect-cards/{id}/handled', { params: { path: { id } }, body: { note: notes[id] || null } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['connect-cards'] }),
  });

  return (
    <>
      <PageHeader
        title="Connect cards"
        subtitle="First-timers, decisions and prayer needs. Please follow up within a few days; prayer requests are confidential."
        actions={
          <Select value={status} onChange={(e) => setStatus(e.target.value as Status | '')}>
            <option value="New">Needs follow-up</option>
            <option value="Handled">Followed up</option>
            <option value="">All</option>
          </Select>
        }
      />
      <ErrorNote error={cards.error ?? handle.error} />
      {cards.isPending ? (
        <Loading />
      ) : cards.data?.length === 0 ? (
        <Card>
          <Empty>{status === 'New' ? 'Everyone has been followed up.' : 'No cards yet.'}</Empty>
        </Card>
      ) : (
        cards.data?.map((c) => (
          <Card
            key={c.id}
            title={
              <>
                <Link to={`/people/${c.personId}`}>{c.personName}</Link> {c.isNewPerson && <Badge tone="accent">New to us</Badge>}
              </>
            }
            actions={<span className="small muted">{formatDateTime(c.submittedAt)} · {c.source}</span>}>
            <p className="row">
              {c.reasons.map((r) => (
                <Badge key={r}>{reasonLabels[r]}</Badge>
              ))}
            </p>
            {c.message && <p className="quote">{c.message}</p>}
            <p className="small">
              {c.mobile && <a href={`tel:${c.mobile}`}>{c.mobile}</a>}
              {c.mobile && c.email && ' · '}
              {c.email && <a href={`mailto:${c.email}`}>{c.email}</a>}
            </p>
            {c.status === 'New' && can(access, Permissions.peopleEdit) ? (
              <div className="row">
                <Field label="Follow-up note">
                  <TextInput placeholder="e.g. Called Monday, coming to Growth Track" value={notes[c.id] ?? ''} onChange={(e) => setNotes({ ...notes, [c.id]: e.target.value })} />
                </Field>
                <Button variant="primary" busy={handle.isPending && handle.variables === c.id} onClick={() => handle.mutate(c.id)}>
                  Mark followed up
                </Button>
              </div>
            ) : (
              c.handledAt && (
                <p className="small muted">
                  Followed up {formatDateTime(c.handledAt)}
                  {c.handlerNote && `: ${c.handlerNote}`}
                </p>
              )
            )}
          </Card>
        ))
      )}
    </>
  );
}
