import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';

type Prayer = Schemas['PrayerAdminDto'];
type Status = Schemas['PrayerStatus'];

const statusLabel: Record<Status, string> = {
  AwaitingReview: 'Waiting for review',
  OnWall: 'On the wall',
  WithPastors: 'With pastors',
  Closed: 'Closed',
};
const statusTone = { AwaitingReview: 'accent', OnWall: 'success', WithPastors: 'neutral', Closed: 'neutral' } as const;

/** Prayer: review requests for the members' wall, and (for pastors) see every request. Both lists are audited. */
export function PrayerPage() {
  const { data: access } = useAccess();
  const canModerate = can(access, Permissions.prayerModerate);
  const canView = can(access, Permissions.prayerView);

  return (
    <>
      <PageHeader
        title="Prayer"
        subtitle="Requests are special personal information. Opening these lists is recorded in the audit log."
      />
      <div className="grid-2">
        {canModerate && <ReviewQueue />}
        {canView && <AllRequests />}
      </div>
    </>
  );
}

function ReviewQueue() {
  const queue = useQuery({ queryKey: ['prayer', 'review'], queryFn: async () => unwrap(await api.GET('/api/admin/prayer/review')) });

  return (
    <Card title="Waiting for the wall">
      <div className="stack">
        <p className="small muted">
          Before approving, remove other people's names and any health details they might not want shared. The requester keeps their original words.
        </p>
        <ErrorNote error={queue.error} />
        {queue.isPending ? (
          <Loading />
        ) : queue.data?.length === 0 ? (
          <Empty>Nothing waiting. Thank you!</Empty>
        ) : (
          queue.data?.map((p) => <ReviewItem key={p.id} prayer={p} />)
        )}
      </div>
    </Card>
  );
}

function ReviewItem({ prayer }: { prayer: Prayer }) {
  const queryClient = useQueryClient();
  const [wallText, setWallText] = useState(prayer.text);
  const [note, setNote] = useState('');
  const done = () => void queryClient.invalidateQueries({ queryKey: ['prayer'] });
  const path = { params: { path: { id: prayer.id } } };

  const approve = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/prayer/{id}/approve', { ...path, body: { wallText, note: null } })),
    onSuccess: done,
  });
  const keep = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/prayer/{id}/keep-with-pastors', { ...path, body: { wallText: null, note: note || null } })),
    onSuccess: done,
  });

  return (
    <div className="stack review-item">
      <div className="row">
        <strong>{prayer.personName}</strong>
        <span className="small muted">
          {formatDateTime(prayer.createdAt)}
          {prayer.anonymous && ' · anonymous on the wall'}
        </span>
      </div>
      <p className="small muted">They wrote: “{prayer.text}”</p>
      <Field label="The wall will show">
        <textarea className="input" rows={3} maxLength={1000} value={wallText} onChange={(e) => setWallText(e.target.value)} />
      </Field>
      <Field label="Note to them if it stays with the pastors" hint="Optional. For example: it names someone else.">
        <textarea className="input" rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      <ErrorNote error={approve.error ?? keep.error} />
      <div className="row">
        <Button variant="primary" busy={approve.isPending} disabled={!wallText.trim()} onClick={() => approve.mutate()}>
          Put on the wall
        </Button>
        <Button busy={keep.isPending} onClick={() => keep.mutate()}>
          Keep with pastors
        </Button>
      </div>
    </div>
  );
}

function AllRequests() {
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const [status, setStatus] = useState<Status | ''>('');
  const list = useQuery({
    queryKey: ['prayer', 'all', status],
    queryFn: async () => unwrap(await api.GET('/api/admin/prayer', { params: { query: { status: status || undefined } } })),
  });
  const takeDown = useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/admin/prayer/{id}/take-down', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['prayer'] }),
  });

  return (
    <Card
      title="All requests"
      actions={
        <Select value={status} onChange={(e) => setStatus(e.target.value as Status | '')} aria-label="Filter by status">
          <option value="">Everything</option>
          {(Object.keys(statusLabel) as Status[]).map((s) => (
            <option key={s} value={s}>
              {statusLabel[s]}
            </option>
          ))}
        </Select>
      }>
      <ErrorNote error={list.error ?? takeDown.error} />
      {list.isPending ? (
        <Loading />
      ) : list.data?.length === 0 ? (
        <Empty>No requests.</Empty>
      ) : (
        <ul className="list">
          {list.data?.map((p) => (
            <li key={p.id} className="stack review-item">
              <div className="row">
                <strong>{p.personName}</strong>
                <Badge tone={statusTone[p.status]}>{statusLabel[p.status]}</Badge>
                {p.visibility === 'PastorsOnly' && <Badge>Pastors only</Badge>}
                {p.source === 'ConnectCard' && <Badge>Connect card</Badge>}
              </div>
              <p>{p.text}</p>
              {p.wallText && p.wallText !== p.text && <p className="small muted">On the wall as: “{p.wallText}”</p>}
              {p.answeredAt && <p className="small">Answered {formatDateTime(p.answeredAt)}{p.answerNote && `: ${p.answerNote}`}</p>}
              <div className="row small muted">
                <span>{formatDateTime(p.createdAt)}</span>
                {p.prayedCount > 0 && <span>{p.prayedCount} prayed</span>}
                {p.status === 'OnWall' && can(access, Permissions.prayerModerate) && (
                  <Button variant="ghost" busy={takeDown.isPending} onClick={() => confirm('Take this request off the wall?') && takeDown.mutate(p.id)}>
                    Take off the wall
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
