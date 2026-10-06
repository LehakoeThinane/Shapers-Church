import { useQuery } from '@tanstack/react-query';
import { Badge, Card, ErrorNote, Loading, PageHeader } from '../components/ui';
import { api, apiBaseUrl, formatDateTime, unwrap } from '../lib/api';

/** Each part of the system under the name staff know it by in the menu. */
const areaLabel: Record<string, string> = {
  assist: 'AI help',
  church: 'Church setup',
  communications: 'Announcements and notifications',
  content: 'Website & app',
  events: 'Events',
  groups: 'Home cells',
  identity: 'Sign-in and access',
  media: 'Sermons & live',
  people: 'People',
  prayer: 'Prayer',
  privacy: 'Privacy (POPIA)',
  services: 'Services',
};

/** "Shapers.Services.Contracts.ServingRequestedIntegrationEvent" becomes "Serving requested". */
function eventLabel(type: string) {
  const name = (type.split('.').pop() ?? type).replace(/IntegrationEvent$/, '');
  const words = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** "events-reminders" becomes "Events reminders". */
function jobLabel(id: string) {
  const words = id.replace(/-/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** Staff with the jobs permission: background work that failed quietly, and where to look next. */
export function BackgroundWorkPage() {
  const health = useQuery({
    queryKey: ['background-health'],
    queryFn: async () => unwrap(await api.GET('/api/admin/background-health')),
    refetchInterval: 60_000,
  });

  const header = (
    <PageHeader
      title="Background work"
      subtitle="Deliveries, reminders and clean-ups that run behind the scenes. When something here fails, nobody sees an error, so it shows up on this page."
      actions={
        <a className="btn btn-secondary" href={`${apiBaseUrl}/jobs`} target="_blank" rel="noopener noreferrer">
          Open the jobs dashboard
        </a>
      }
    />
  );

  if (health.isPending) {
    return (
      <>
        {header}
        <Loading />
      </>
    );
  }

  if (!health.data) {
    return (
      <>
        {header}
        <ErrorNote error={health.error} />
      </>
    );
  }

  const h = health.data;
  const areas = h.outbox.filter((o) => o.waiting + o.gaveUp > 0);
  const messages = areas.reduce((n, o) => n + o.waiting + o.gaveUp, 0);
  const failedJobs = Number(h.jobs?.failed ?? 0);
  const stopped = h.jobs !== null && h.jobs !== undefined && !h.jobs.running;
  const things = messages + failedJobs + (stopped ? 1 : 0);

  return (
    <>
      {header}

      <Card>
        <div className="row">
          {h.needsAttention ? <Badge tone="danger">Needs attention</Badge> : <Badge tone="success">All good</Badge>}
          <strong>{h.needsAttention ? `${plural(things, 'thing needs', 'things need')} attention` : 'Everything is running.'}</strong>
        </div>
        <p className="small muted">
          Checked {formatDateTime(h.checkedAt)}. This page refreshes every minute; the team is emailed if a problem lasts.
        </p>
        {!h.jobs && <p className="note small">Background jobs are switched off on this server, so only deliveries are checked.</p>}
        {stopped && (
          <p className="note note-danger" role="alert">
            The job server has stopped: reminders, scheduled publishing and clean-ups aren't running. Restart the API (see "Everyday tasks" in docs/deployment.md).
          </p>
        )}
      </Card>

      {areas.length > 0 && (
        <Card title="Waiting to be delivered">
          <p className="small muted">
            Work passed from one part of the system to another, for example asking a volunteer to serve. Waiting ones usually go through on their own. Ones that were given up on need a developer: the
            cause is in the API logs at the time shown.
          </p>
          <table className="table">
            <thead>
              <tr>
                <th>Area</th>
                <th>What</th>
                <th>Since</th>
                <th>Tries</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {areas.flatMap((o) =>
                o.oldest.map((p) => (
                  <tr key={p.id}>
                    <td>{areaLabel[o.module] ?? o.module}</td>
                    <td>
                      {eventLabel(p.eventType)}
                      {p.errorType && <span className="small muted"> · {p.errorType}</span>}
                    </td>
                    <td className="small">{formatDateTime(p.occurredAt)}</td>
                    <td className="small">{p.attempts}</td>
                    <td>{p.gaveUp ? <Badge tone="danger">Given up</Badge> : <Badge tone="accent">Waiting</Badge>}</td>
                  </tr>
                )),
              )}
            </tbody>
          </table>
          {areas.some((o) => o.waiting + o.gaveUp > o.oldest.length) && <p className="small muted">Only the oldest ten in each area are listed.</p>}
        </Card>
      )}

      {h.jobs && failedJobs > 0 && (
        <Card title="Jobs that failed">
          <p className="small muted">These failed after all their retries. Open the jobs dashboard to try one again or remove it.</p>
          <table className="table">
            <thead>
              <tr>
                <th>Job</th>
                <th>Failed</th>
                <th>Error</th>
              </tr>
            </thead>
            <tbody>
              {h.jobs.recentFailures.map((f, i) => (
                <tr key={i}>
                  <td>{jobLabel(f.job)}</td>
                  <td className="small">{f.failedAt ? formatDateTime(f.failedAt) : '—'}</td>
                  <td className="small muted">{f.errorType ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {failedJobs > h.jobs.recentFailures.length && <p className="small muted">Showing the latest {h.jobs.recentFailures.length} of {failedJobs}.</p>}
        </Card>
      )}
    </>
  );
}
