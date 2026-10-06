import { useQuery } from '@tanstack/react-query';
import { Badge, Card, Empty, ErrorNote, Loading, PageHeader } from '../components/ui';
import { api, formatDateTime, unwrap } from '../lib/api';

const rand = (value: number) => new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(value);

const purposeLabel: Record<string, string> = {
  'sermon-lesson': 'Cell lesson from a sermon',
  'sermon-notes': 'Sermon summary and notes',
  'sermon-transcript': 'Sermon transcription',
  rewrite: 'Writing help',
};

/** Administrators: what AI help is used for and what it costs. The log holds no content, only counts. */
export function AssistUsagePage() {
  const usage = useQuery({ queryKey: ['assist', 'usage'], queryFn: async () => unwrap(await api.GET('/api/admin/assist/usage')) });

  if (usage.isPending) return <Loading />;
  if (!usage.data) return <ErrorNote error={usage.error} />;
  const u = usage.data;
  const share = u.budgetZar > 0 ? Math.min(1, u.spentThisMonthZar / u.budgetZar) : 1;

  return (
    <>
      <PageHeader
        title="AI help"
        subtitle="AI drafts sermon notes, cell lessons and writing for staff to review. It never sees cell reports, prayer requests, pastoral notes, chat or people's details."
      />
      <div className="grid-2">
        <Card title="This month">
          {!u.enabled ? (
            <p className="note">AI help is switched off. Set Assist:Provider to Azure in the server settings to turn it on.</p>
          ) : (
            <div className="stack">
              <p>
                <strong>{rand(u.spentThisMonthZar)}</strong> of {rand(u.budgetZar)} (estimated)
              </p>
              <progress max={1} value={share} aria-label="Share of the monthly budget used" />
              {share >= 1 && <p className="note note-danger small">The budget is used up: AI help is paused until the 1st.</p>}
              <p className="small muted">
                Model: {u.chatModel}. Estimates use the prices in the server settings; the Azure invoice is the real figure.
              </p>
            </div>
          )}
        </Card>
        <Card title="Last six months">
          <table className="table">
            <thead>
              <tr>
                <th>Month</th>
                <th>Requests</th>
                <th>Cost</th>
              </tr>
            </thead>
            <tbody>
              {u.months.map((m) => (
                <tr key={m.month}>
                  <td>{m.month}</td>
                  <td>{m.calls}</td>
                  <td>{rand(m.costZar)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      </div>
      <Card title="Recent requests">
        {u.recent.length === 0 ? (
          <Empty>Nothing yet.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>When</th>
                <th>For</th>
                <th>Size</th>
                <th>Cost</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {u.recent.map((r, i) => (
                <tr key={i}>
                  <td className="small">{formatDateTime(r.occurredAt)}</td>
                  <td>{purposeLabel[r.purpose] ?? r.purpose}</td>
                  <td className="small">
                    {r.operation === 'Transcription' ? `${Math.round(r.audioSeconds / 60)} min of audio` : `${(r.inputTokens + r.outputTokens).toLocaleString('en-ZA')} tokens`}
                  </td>
                  <td className="small">{rand(r.costZar)}</td>
                  <td>{!r.succeeded && <Badge tone="danger">Failed</Badge>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}
