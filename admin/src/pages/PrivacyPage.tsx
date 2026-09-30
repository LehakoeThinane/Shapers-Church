import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select } from '../components/ui';
import { api, formatDate, formatDateTime, unwrap, type Schemas } from '../lib/api';

type Request = Schemas['DataRequestAdminDto'];
type Status = Schemas['DataRequestStatus'];

/** The Information Officer's queue: correction and deletion requests under POPIA. */
export function PrivacyPage() {
  const [status, setStatus] = useState<Status | ''>('Open');
  const requests = useQuery({
    queryKey: ['privacy-requests', status],
    queryFn: async () => unwrap(await api.GET('/api/admin/privacy/requests', { params: { query: { status: status || undefined } } })),
  });

  return (
    <>
      <PageHeader
        title="Privacy requests"
        subtitle={
          <>
            Corrections and deletions people have asked for. Answer within 30 days. Who read sensitive records is in the <Link to="/audit">audit log</Link>.
          </>
        }
        actions={
          <Select value={status} onChange={(e) => setStatus(e.target.value as Status | '')} aria-label="Filter by status">
            <option value="Open">Open</option>
            <option value="Completed">Completed</option>
            <option value="Declined">Declined</option>
            <option value="">Everything</option>
          </Select>
        }
      />
      <ErrorNote error={requests.error} />
      {requests.isPending ? (
        <Loading />
      ) : requests.data?.length === 0 ? (
        <Card>
          <Empty>No requests here.</Empty>
        </Card>
      ) : (
        <div className="stack">
          {requests.data?.map((r) => (
            <RequestCard key={r.id} request={r} />
          ))}
        </div>
      )}
    </>
  );
}

function RequestCard({ request: r }: { request: Request }) {
  const queryClient = useQueryClient();
  const [response, setResponse] = useState('');
  const done = () => void queryClient.invalidateQueries({ queryKey: ['privacy-requests'] });
  const path = { params: { path: { id: r.id } } };
  const complete = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/privacy/requests/{id}/complete', { ...path, body: { response: response || null } })),
    onSuccess: done,
  });
  const decline = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/privacy/requests/{id}/decline', { ...path, body: { response } })),
    onSuccess: done,
  });
  const deletion = r.type === 'Deletion';

  return (
    <Card
      title={
        <span className="row">
          {deletion ? 'Delete their data' : 'Correct their details'}
          {r.overdue && <Badge tone="danger">Overdue</Badge>}
          {r.status !== 'Open' && <Badge tone={r.status === 'Completed' ? 'success' : 'neutral'}>{r.status}</Badge>}
        </span>
      }>
      <div className="stack">
        <p>
          <Link to={`/people/${r.personId}`}>{r.personName}</Link>
          <span className="small muted">
            {' '}
            · asked {formatDateTime(r.createdAt)} · {r.status === 'Open' ? `answer by ${formatDate(r.dueAt)}` : `decided ${formatDateTime(r.decidedAt)}`}
          </span>
        </p>
        {r.details && <p>“{r.details}”</p>}
        {r.response && <p className="small muted">Our answer: {r.response}</p>}
        {r.status === 'Open' && (
          <>
            {deletion ? (
              <p className="small">
                Completing this deletes their login, prayer requests, notifications and phones, removes their name from event bookings and
                households, and empties their church record. It can&rsquo;t be undone. Decline instead if the law requires us to keep something, and
                say why.
              </p>
            ) : (
              <p className="small">
                Make the correction on their <Link to={`/people/${r.personId}`}>record</Link>, then mark this done.
              </p>
            )}
            <Field label="Message to them" hint={deletion ? 'Optional when completing; required when declining.' : 'Say what you corrected, or why not.'}>
              <textarea className="input" rows={2} maxLength={2000} value={response} onChange={(e) => setResponse(e.target.value)} />
            </Field>
            <ErrorNote error={complete.error ?? decline.error} />
            <div className="row">
              <Button
                variant={deletion ? 'danger' : 'primary'}
                busy={complete.isPending}
                onClick={() => (!deletion || confirm(`Erase ${r.personName} everywhere? This can't be undone.`)) && complete.mutate()}>
                {deletion ? 'Erase their data' : 'Mark corrected'}
              </Button>
              <Button busy={decline.isPending} disabled={!response.trim()} onClick={() => decline.mutate()}>
                Decline
              </Button>
            </div>
          </>
        )}
      </div>
    </Card>
  );
}
