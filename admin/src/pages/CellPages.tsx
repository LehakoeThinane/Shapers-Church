import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, ApiError, formatDate, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { days, meetingLabel, multiplicationLabel, nextStepLabel, roleLabel, today, type CellRole, type DayOfWeek } from '../lib/cells';
import { useCampuses } from '../lib/queries';

type Cell = Schemas['CellDetailDto'];
type Report = Schemas['ReportDto'];

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/** Pastors: every cell at a glance. Missing reports and urgent follow-ups stand out. */
export function CellsPage() {
  const { data: access } = useAccess();
  const cells = useQuery({ queryKey: ['cells'], queryFn: async () => unwrap(await api.GET('/api/admin/cells')) });
  const [adding, setAdding] = useState(false);
  const missing = cells.data?.filter((c) => !c.reportedThisWeek).length ?? 0;
  const urgent = cells.data?.reduce((sum, c) => sum + c.openUrgentFollowUps, 0) ?? 0;

  return (
    <>
      <PageHeader
        title="Cells"
        subtitle="Home cells, their leaders, and how each one is doing this week."
        actions={
          can(access, Permissions.cellsManage) && (
            <Button variant="primary" onClick={() => setAdding((a) => !a)}>
              {adding ? 'Close' : 'New cell'}
            </Button>
          )
        }
      />
      {adding && <CellForm onSaved={() => setAdding(false)} />}

      {cells.data && cells.data.length > 0 && (
        <div className="task-grid">
          <div className="task glass">
            <span className="task-count">{cells.data.length}</span>
            <span className="stack-tight">
              <strong>Active cells</strong>
              <span className="small muted">{cells.data.reduce((s, c) => s + c.memberCount, 0)} people in cells</span>
            </span>
          </div>
          <div className={`task glass${missing === 0 ? ' task-done' : ''}`}>
            <span className="task-count">{missing === 0 ? '✓' : missing}</span>
            <span className="stack-tight">
              <strong>Reports missing this week</strong>
              <span className="small muted">{missing === 0 ? 'Every cell has reported' : 'No report in the last 7 days'}</span>
            </span>
          </div>
          <Link to="/cells/reports?urgent=1" className={`task glass${urgent === 0 ? ' task-done' : ''}`}>
            <span className="task-count">{urgent === 0 ? '✓' : urgent}</span>
            <span className="stack-tight">
              <strong>Urgent follow-ups</strong>
              <span className="small muted">{urgent === 0 ? 'All caught up' : 'Flagged by leaders, not yet resolved'}</span>
            </span>
          </Link>
        </div>
      )}

      <Card>
        <ErrorNote error={cells.error} />
        {cells.isPending ? (
          <Loading />
        ) : cells.data?.length === 0 ? (
          <Empty>No cells yet. Create the first one and choose its leader.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Cell</th>
                <th>Leaders</th>
                <th>Meets</th>
                <th>Members</th>
                <th>Last report</th>
                <th>This week</th>
              </tr>
            </thead>
            <tbody>
              {cells.data?.map((c) => (
                <tr key={c.id}>
                  <td>
                    <Link to={`/cells/${c.id}`}>{c.name}</Link>
                    {c.area && <span className="small muted"> · {c.area}</span>}
                  </td>
                  <td>{c.leaders.join(', ') || <span className="danger-text">No leader</span>}</td>
                  <td className="small">{meetingLabel(c.meetingDay, c.meetingTime)}</td>
                  <td>{c.memberCount}</td>
                  <td className="small">{c.lastReportDate ? `${formatDate(c.lastReportDate)} · ${c.lastAttendance} came` : 'None yet'}</td>
                  <td>
                    {c.reportedThisWeek ? <Badge tone="success">Reported</Badge> : <Badge tone="danger">Missing</Badge>}
                    {c.openUrgentFollowUps > 0 && <Badge tone="accent">{c.openUrgentFollowUps} urgent</Badge>}
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

function CellForm({ cell, onSaved }: { cell?: Cell; onSaved: (cell: Cell) => void }) {
  const queryClient = useQueryClient();
  const campuses = useCampuses();
  const [form, setForm] = useState({
    name: cell?.name ?? '',
    campusId: cell?.campusId ?? '',
    meetingDay: (cell?.meetingDay ?? '') as DayOfWeek | '',
    meetingTime: cell?.meetingTime?.slice(0, 5) ?? '',
    area: cell?.area ?? '',
    address: cell?.address ?? '',
  });
  const campusId = form.campusId || campuses.data?.find((c) => c.isPrimary)?.id || '';
  const save = useMutation({
    mutationFn: async () => {
      const body = {
        name: form.name,
        campusId,
        meetingDay: form.meetingDay || null,
        meetingTime: form.meetingTime ? `${form.meetingTime}:00` : null,
        area: form.area || null,
        address: form.address || null,
      };
      return cell
        ? unwrap(await api.PUT('/api/admin/cells/{id}', { params: { path: { id: cell.id } }, body }))
        : unwrap(await api.POST('/api/admin/cells', { body }));
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['cells'] });
      queryClient.setQueryData(['cell', saved.id], saved);
      onSaved(saved);
    },
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <Card title={cell ? 'Cell details' : 'New cell'}>
      <form className="form-grid" onSubmit={submit}>
        <Field label="Name">
          <TextInput value={form.name} onChange={set('name')} required maxLength={80} placeholder="e.g. Rivonia North" />
        </Field>
        <Field label="Campus">
          <Select value={campusId} onChange={set('campusId')} disabled={!!cell}>
            {campuses.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Meets on">
          <Select value={form.meetingDay} onChange={set('meetingDay')}>
            <option value="">Not set</option>
            {days.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Time">
          <TextInput type="time" value={form.meetingTime} onChange={set('meetingTime')} />
        </Field>
        <Field label="Area" hint="Safe to show anyone looking for a cell.">
          <TextInput value={form.area} onChange={set('area')} maxLength={100} placeholder="e.g. Rivonia" />
        </Field>
        <Field label="Address" hint="Only the cell's members, leaders and pastors see this.">
          <TextInput value={form.address} onChange={set('address')} maxLength={300} />
        </Field>
        <div className="form-actions">
          <ErrorNote error={save.error} />
          <Button variant="primary" type="submit" busy={save.isPending}>
            {cell ? 'Save' : 'Create cell'}
          </Button>
        </div>
      </form>
    </Card>
  );
}

/** Pastors: one cell's details, leaders and members, and its recent reports. */
export function CellPage() {
  const { id = '' } = useParams();
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const manage = can(access, Permissions.cellsManage);
  const cell = useQuery({ queryKey: ['cell', id], queryFn: async () => unwrap(await api.GET('/api/admin/cells/{id}', { params: { path: { id } } })) });
  const reports = useQuery({
    queryKey: ['cell-reports', id],
    queryFn: async () => unwrap(await api.GET('/api/admin/cells/reports', { params: { query: { cellId: id } } })),
    enabled: can(access, Permissions.cellReportsView),
  });
  const remove = useMutation({
    mutationFn: async (personId: string) => unwrap(await api.DELETE('/api/admin/cells/{id}/members/{personId}', { params: { path: { id, personId } } })),
    onSuccess: (saved) => queryClient.setQueryData(['cell', id], saved),
  });
  const close = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/cells/{id}/close', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['cells'] });
      navigate('/cells');
    },
  });

  if (cell.isPending) return <Loading />;
  if (!cell.data) return <ErrorNote error={cell.error} />;
  const c = cell.data;

  return (
    <>
      <PageHeader
        title={c.name}
        subtitle={`${meetingLabel(c.meetingDay, c.meetingTime)}${c.area ? ` · ${c.area}` : ''}`}
        actions={
          manage && (
            <Button
              variant="danger"
              busy={close.isPending}
              onClick={() => window.confirm(`Close ${c.name}? Its reports stay for the pastors.`) && close.mutate()}
            >
              Close cell
            </Button>
          )
        }
      />
      <div className="grid-2">
        <Card title="Leaders and members">
          <ErrorNote error={remove.error} />
          {c.members.length === 0 ? (
            <Empty>No one yet. Add a leader first.</Empty>
          ) : (
            <ul className="list">
              {c.members.map((m) => (
                <li key={m.personId} className="list-row">
                  <span>
                    <Link to={`/people/${m.personId}`}>{m.name}</Link>
                    {m.role !== 'Member' && <Badge tone="accent">{roleLabel[m.role]}</Badge>}
                  </span>
                  {manage && (
                    <button type="button" className="link-button" onClick={() => remove.mutate(m.personId)}>
                      Remove
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
          {manage && <AddMember cellId={id} />}
          {manage && (
            <p className="small muted">
              Leaders sign in to the admin portal to record meetings. If a leader has no login yet, open their record in People and create a staff login.
            </p>
          )}
        </Card>
        {manage && <CellForm cell={c} onSaved={() => undefined} />}
      </div>
      {reports.data && (
        <Card title="Reports">
          <ReportList reports={reports.data} />
        </Card>
      )}
    </>
  );
}

function AddMember({ cellId }: { cellId: string }) {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [role, setRole] = useState<CellRole>('Member');
  const people = useQuery({
    queryKey: ['people-pick', search],
    queryFn: async () => unwrap(await api.GET('/api/admin/people', { params: { query: { Search: search, Page: 1, PageSize: 8 } } })),
    enabled: search.trim().length >= 2,
  });
  const add = useMutation({
    mutationFn: async (personId: string) => unwrap(await api.POST('/api/admin/cells/{id}/members', { params: { path: { id: cellId } }, body: { personId, role } })),
    onSuccess: (saved) => {
      queryClient.setQueryData(['cell', cellId], saved);
      void queryClient.invalidateQueries({ queryKey: ['cells'] });
      setSearch('');
    },
  });

  return (
    <div className="stack">
      <div className="row">
        <TextInput placeholder="Find someone by name, email or phone" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Find a person" />
        <Select value={role} onChange={(e) => setRole(e.target.value as CellRole)} aria-label="Role">
          <option value="Member">Member</option>
          <option value="Leader">Leader</option>
          <option value="CoLeader">Co-leader</option>
        </Select>
      </div>
      <ErrorNote error={people.error ?? add.error} />
      {people.data && (
        <ul className="list">
          {people.data.items.map((p) => (
            <li key={p.id} className="list-row">
              <span>
                {p.displayName} <span className="small muted">{p.primaryMobile ?? p.primaryEmail ?? ''}</span>
              </span>
              <button type="button" className="link-button" onClick={() => add.mutate(p.id)}>
                Add as {roleLabel[role].toLowerCase()}
              </button>
            </li>
          ))}
          {people.data.items.length === 0 && <Empty>No one matches.</Empty>}
        </ul>
      )}
    </div>
  );
}

function ReportList({ reports, base = '/cells/reports' }: { reports: Schemas['ReportSummaryDto'][]; base?: string }) {
  if (reports.length === 0) return <Empty>No reports yet.</Empty>;
  return (
    <table className="table">
      <thead>
        <tr>
          <th>Meeting</th>
          <th>Cell</th>
          <th>Topic</th>
          <th>Came</th>
          <th>By</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        {reports.map((r) => (
          <tr key={r.id}>
            <td>
              <Link to={`${base}/${r.id}`}>{formatDate(r.meetingDate)}</Link>
            </td>
            <td>{r.cellName}</td>
            <td>{r.topic ?? '—'}</td>
            <td>
              {r.membersPresent}
              {r.visitorCount > 0 && <span className="small muted"> + {plural(r.visitorCount, 'visitor')}</span>}
            </td>
            <td className="small">{r.writtenBy}</td>
            <td>
              {r.status === 'Draft' && <Badge>Draft</Badge>}
              {r.openUrgentFollowUps > 0 && <Badge tone="accent">{r.openUrgentFollowUps} urgent</Badge>}
              {r.redacted && <Badge>Archived</Badge>}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Pastors: every submitted report they may read. Opening one is recorded in the audit log. */
export function CellReportsPage() {
  const urgentOnly = new URLSearchParams(window.location.search).get('urgent') === '1';
  const reports = useQuery({
    queryKey: ['cell-reports', 'all', urgentOnly],
    queryFn: async () => unwrap(await api.GET('/api/admin/cells/reports', { params: { query: { urgentOnly } } })),
  });

  return (
    <>
      <PageHeader
        title={urgentOnly ? 'Urgent follow-ups' : 'Cell reports'}
        subtitle={
          <>
            Reports from cell leaders after each meeting. They can hold personal and pastoral details: who opens them is recorded in the{' '}
            <Link to="/audit">audit log</Link>, and the personal parts are removed after a year.
          </>
        }
        actions={
          <Link className="btn btn-secondary" to={urgentOnly ? '/cells/reports' : '/cells/reports?urgent=1'} reloadDocument>
            {urgentOnly ? 'All reports' : 'Urgent only'}
          </Link>
        }
      />
      <Card>
        <ErrorNote error={reports.error} />
        {reports.isPending ? <Loading /> : <ReportList reports={reports.data ?? []} />}
      </Card>
    </>
  );
}

export function CellReportPage() {
  const { reportId = '' } = useParams();
  const queryClient = useQueryClient();
  const report = useQuery({
    queryKey: ['cell-report', reportId],
    queryFn: async () => unwrap(await api.GET('/api/admin/cells/reports/{id}', { params: { path: { id: reportId } } })),
  });
  const resolve = useMutation({
    mutationFn: async (followUpId: string) =>
      unwrap(await api.POST('/api/admin/cells/reports/{id}/follow-ups/{followUpId}/resolve', { params: { path: { id: reportId, followUpId } } })),
    onSuccess: (saved) => {
      queryClient.setQueryData(['cell-report', reportId], saved);
      void queryClient.invalidateQueries({ queryKey: ['cells'] });
      void queryClient.invalidateQueries({ queryKey: ['cell-reports'] });
    },
  });

  if (report.isPending) return <Loading />;
  if (!report.data) return <ErrorNote error={report.error} />;
  return (
    <>
      <PageHeader title={`${report.data.cellName}, ${formatDate(report.data.meetingDate)}`} subtitle={`Written by ${report.data.writtenBy}`} />
      <ErrorNote error={resolve.error} />
      <ReportView report={report.data} onResolve={(id) => resolve.mutate(id)} />
    </>
  );
}

/** A report, read-only. Pastors can mark follow-ups as dealt with. */
export function ReportView({ report: r, onResolve }: { report: Report; onResolve?: (followUpId: string) => void }) {
  return (
    <div className="grid-2">
      <Card title="The meeting">
        <dl className="stack">
          <div>
            <dt className="field-label">Topic</dt>
            <dd>{r.topic ?? '—'}{r.materialTitle && <span className="small muted"> · from “{r.materialTitle}”</span>}</dd>
          </div>
          {r.notes && (
            <div>
              <dt className="field-label">How it went</dt>
              <dd className="quote">{r.notes}</dd>
            </div>
          )}
          {r.highlights && (
            <div>
              <dt className="field-label">Highlights and testimonies</dt>
              <dd className="quote">{r.highlights}</dd>
            </div>
          )}
          <div>
            <dt className="field-label">Multiplication</dt>
            <dd>{multiplicationLabel[r.multiplication]}</dd>
          </div>
        </dl>
        {r.redacted && <p className="note">The personal parts of this report were removed after a year. Attendance numbers are kept.</p>}
      </Card>
      <Card title={`Who came (${r.membersPresent}${r.visitorCount ? ` + ${plural(r.visitorCount, 'visitor')}` : ''})`}>
        {r.attendees.length > 0 && <p>{r.attendees.map((a) => a.name).join(', ')}</p>}
        {r.visitors.length > 0 && (
          <ul className="list">
            {r.visitors.map((v, i) => (
              <li key={i} className="list-row">
                <span>
                  {v.firstName} {v.lastName ?? ''} <Badge>Visitor</Badge>
                </span>
                <span className="small muted">{v.agreedToBeContacted ? 'Sent to follow-up as a connect card' : 'Not contacted (no agreement)'}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
      {(r.prayerNeeds || r.followUps.length > 0) && (
        <Card title="Prayer and follow-ups">
          {r.prayerNeeds && <p className="quote">{r.prayerNeeds}</p>}
          <ul className="list">
            {r.followUps.map((f) => (
              <li key={f.id} className="review-item stack">
                <span>
                  <strong>{f.name}</strong> {f.urgent && <Badge tone="accent">Urgent</Badge>} {f.resolvedAt && <Badge tone="success">Dealt with {formatDateTime(f.resolvedAt)}</Badge>}
                </span>
                {f.note && <span>{f.note}</span>}
                {onResolve && !f.resolvedAt && (
                  <Button onClick={() => onResolve(f.id)} className="link-button">
                    Mark as dealt with
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
      {r.growth.length > 0 && (
        <Card title="Ready for a next step">
          <ul className="list">
            {r.growth.map((g) => (
              <li key={`${g.personId}-${g.step}`} className="list-row">
                <span>{g.name}</span>
                <Badge tone="success">{nextStepLabel[g.step]}</Badge>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  );
}

/** The API refuses leaders without two-step sign-in; point them at the fix instead of a bare error. */
function LeaderError({ error }: { error: unknown }) {
  if (error instanceof ApiError && error.code === 'groups.mfa_required') {
    return (
      <p className="note note-accent">
        Cell leaders need two-step sign-in to see their members. <Link to="/security">Set it up</Link>, then sign in again.
      </p>
    );
  }
  return <ErrorNote error={error} />;
}

/** Leaders: their cell's meetings, members and teaching. */
export function MyCellPage() {
  const { cellId = '' } = useParams();
  const cell = useQuery({ queryKey: ['my-cell', cellId], queryFn: async () => unwrap(await api.GET('/api/cells/{cellId}', { params: { path: { cellId } } })) });
  const reports = useQuery({
    queryKey: ['my-cell-reports', cellId],
    queryFn: async () => unwrap(await api.GET('/api/cells/{cellId}/reports', { params: { path: { cellId } } })),
    enabled: cell.isSuccess,
  });

  if (cell.isPending) return <Loading />;
  if (!cell.data) return <LeaderError error={cell.error} />;
  const c = cell.data;

  return (
    <>
      <PageHeader
        title={c.name}
        subtitle={`${meetingLabel(c.meetingDay, c.meetingTime)}${c.address ? ` · ${c.address}` : ''}`}
        actions={
          <Link className="btn btn-primary" to={`/my-cells/${cellId}/reports/new`}>
            Record a meeting
          </Link>
        }
      />
      <Card title="Meetings">
        <ErrorNote error={reports.error} />
        {reports.isPending ? <Loading /> : <ReportList reports={reports.data ?? []} base={`/my-cells/${cellId}/reports`} />}
      </Card>
      <div className="grid-2">
        <LeaderMembers cell={c} />
        <LeaderMaterials cellId={cellId} />
      </div>
    </>
  );
}

function LeaderMembers({ cell: c }: { cell: Cell }) {
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ firstName: '', lastName: '', mobile: '', email: '', agreedToBeOnRecord: false });
  const saved = (cell: Cell) => queryClient.setQueryData(['my-cell', c.id], cell);
  const add = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/cells/{cellId}/members', {
          params: { path: { cellId: c.id } },
          body: { ...form, mobile: form.mobile || null, email: form.email || null },
        }),
      ),
    onSuccess: (cell) => {
      saved(cell);
      setAdding(false);
      setForm({ firstName: '', lastName: '', mobile: '', email: '', agreedToBeOnRecord: false });
    },
  });
  const remove = useMutation({
    mutationFn: async (personId: string) => unwrap(await api.DELETE('/api/cells/{cellId}/members/{personId}', { params: { path: { cellId: c.id, personId } } })),
    onSuccess: saved,
  });
  const set = (key: 'firstName' | 'lastName' | 'mobile' | 'email') => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });

  return (
    <Card
      title={`Members (${c.members.length})`}
      actions={
        <Button onClick={() => setAdding((a) => !a)} className="link-button">
          {adding ? 'Cancel' : 'Add someone'}
        </Button>
      }
    >
      <ErrorNote error={remove.error} />
      <ul className="list">
        {c.members.map((m) => (
          <li key={m.personId} className="list-row">
            <span>
              {m.name} {m.role !== 'Member' && <Badge tone="accent">{roleLabel[m.role]}</Badge>}
            </span>
            {m.role === 'Member' && (
              <button type="button" className="link-button" onClick={() => window.confirm(`Remove ${m.name} from the cell?`) && remove.mutate(m.personId)}>
                Remove
              </button>
            )}
          </li>
        ))}
      </ul>
      {adding && (
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault();
            add.mutate();
          }}
        >
          <div className="row">
            <TextInput placeholder="First name" value={form.firstName} onChange={set('firstName')} required aria-label="First name" />
            <TextInput placeholder="Last name" value={form.lastName} onChange={set('lastName')} required aria-label="Last name" />
          </div>
          <div className="row">
            <TextInput placeholder="Mobile" value={form.mobile} onChange={set('mobile')} aria-label="Mobile" />
            <TextInput placeholder="Email" type="email" value={form.email} onChange={set('email')} aria-label="Email" />
          </div>
          <label className="checkbox">
            <input type="checkbox" checked={form.agreedToBeOnRecord} onChange={(e) => setForm({ ...form, agreedToBeOnRecord: e.target.checked })} />
            They agreed to the church keeping their details (POPIA)
          </label>
          <p className="small muted">A mobile number or email is needed. If they are already on the church's records, the office will merge the two.</p>
          <ErrorNote error={add.error} />
          <Button variant="primary" type="submit" busy={add.isPending} disabled={!form.agreedToBeOnRecord}>
            Add to the cell
          </Button>
        </form>
      )}
    </Card>
  );
}

const emptyMaterial = { title: '', body: '', link: '', forDate: '', sharedWithMembers: false };

function LeaderMaterials({ cellId }: { cellId: string }) {
  const queryClient = useQueryClient();
  const materials = useQuery({
    queryKey: ['my-cell-materials', cellId],
    queryFn: async () => unwrap(await api.GET('/api/cells/{cellId}/materials', { params: { path: { cellId } } })),
  });
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  const [form, setForm] = useState(emptyMaterial);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['my-cell-materials', cellId] });
  const save = useMutation({
    mutationFn: async () => {
      const body = { ...form, link: form.link || null, forDate: form.forDate || null };
      return editing === 'new'
        ? unwrap(await api.POST('/api/cells/{cellId}/materials', { params: { path: { cellId } }, body }))
        : unwrap(await api.PUT('/api/cells/{cellId}/materials/{materialId}', { params: { path: { cellId, materialId: editing! } }, body }));
    },
    onSuccess: () => {
      refresh();
      setEditing(null);
    },
  });
  const remove = useMutation({
    mutationFn: async (materialId: string) => unwrap(await api.DELETE('/api/cells/{cellId}/materials/{materialId}', { params: { path: { cellId, materialId } } })),
    onSuccess: refresh,
  });
  const open = (m?: Schemas['MaterialDto']) => {
    setEditing(m?.id ?? 'new');
    setForm(m ? { title: m.title, body: m.body, link: m.link ?? '', forDate: m.forDate ?? '', sharedWithMembers: m.sharedWithMembers } : emptyMaterial);
  };

  return (
    <Card
      title="Teaching"
      actions={
        editing === null && (
          <Button onClick={() => open()} className="link-button">
            New lesson
          </Button>
        )
      }
    >
      {editing !== null && (
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          <Field label="Title">
            <TextInput value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required maxLength={160} />
          </Field>
          <Field label="Lesson" hint="Scripture, discussion questions, notes.">
            <textarea className="input" rows={8} value={form.body} onChange={(e) => setForm({ ...form, body: e.target.value })} required />
          </Field>
          <div className="row">
            <Field label="Link (optional)">
              <TextInput type="url" value={form.link} onChange={(e) => setForm({ ...form, link: e.target.value })} placeholder="https://" />
            </Field>
            <Field label="For the meeting on">
              <TextInput type="date" value={form.forDate} onChange={(e) => setForm({ ...form, forDate: e.target.value })} />
            </Field>
          </div>
          <label className="checkbox">
            <input type="checkbox" checked={form.sharedWithMembers} onChange={(e) => setForm({ ...form, sharedWithMembers: e.target.checked })} />
            Members can read it in the app
          </label>
          <ErrorNote error={save.error} />
          <div className="row">
            <Button variant="primary" type="submit" busy={save.isPending}>
              Save
            </Button>
            <Button onClick={() => setEditing(null)}>Cancel</Button>
          </div>
        </form>
      )}
      <ErrorNote error={materials.error ?? remove.error} />
      {materials.data?.length === 0 && editing === null && <Empty>No lessons yet. Pastors can see what you write here.</Empty>}
      <ul className="list">
        {materials.data?.map((m) => (
          <li key={m.id} className="list-row">
            <span>
              {m.title} {m.sharedWithMembers && <Badge tone="success">Shared</Badge>}
              <span className="small muted"> {m.forDate ? `for ${formatDate(m.forDate)}` : `updated ${formatDate(m.updatedAt)}`}</span>
            </span>
            <span className="row">
              <button type="button" className="link-button" onClick={() => open(m)}>
                Edit
              </button>
              <button type="button" className="link-button" onClick={() => window.confirm(`Delete “${m.title}”?`) && remove.mutate(m.id)}>
                Delete
              </button>
            </span>
          </li>
        ))}
      </ul>
    </Card>
  );
}

type Visitor = Schemas['ReportVisitor'];
type FollowUp = Schemas['ReportFollowUp'];
type NextStep = Schemas['NextStep'];

/** Leaders: write up a meeting. Drafts can be saved and finished later; submitting sends it to the pastors. */
export function ReportEditorPage() {
  const { cellId = '', reportId } = useParams();
  const isNew = !reportId || reportId === 'new';
  const cell = useQuery({ queryKey: ['my-cell', cellId], queryFn: async () => unwrap(await api.GET('/api/cells/{cellId}', { params: { path: { cellId } } })) });
  const report = useQuery({
    queryKey: ['my-cell-report', reportId],
    queryFn: async () => unwrap(await api.GET('/api/cells/{cellId}/reports/{reportId}', { params: { path: { cellId, reportId: reportId! } } })),
    enabled: !isNew,
  });
  const materials = useQuery({
    queryKey: ['my-cell-materials', cellId],
    queryFn: async () => unwrap(await api.GET('/api/cells/{cellId}/materials', { params: { path: { cellId } } })),
  });

  if (cell.isPending || (!isNew && report.isPending)) return <Loading />;
  if (!cell.data) return <LeaderError error={cell.error} />;
  if (!isNew && !report.data) return <LeaderError error={report.error} />;

  if (report.data?.status === 'Submitted') {
    return (
      <>
        <PageHeader title={`${report.data.cellName}, ${formatDate(report.data.meetingDate)}`} subtitle="Submitted to the pastors." />
        <ReportView report={report.data} />
      </>
    );
  }
  return <ReportForm cell={cell.data} report={report.data} materials={materials.data ?? []} />;
}

function ReportForm({ cell, report, materials }: { cell: Cell; report?: Report; materials: Schemas['MaterialDto'][] }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    meetingDate: report?.meetingDate ?? today(),
    topic: report?.topic ?? '',
    materialId: report?.materialId ?? '',
    notes: report?.notes ?? '',
    highlights: report?.highlights ?? '',
    prayerNeeds: report?.prayerNeeds ?? '',
    multiplication: report?.multiplication ?? ('NotYet' as Schemas['MultiplicationReadiness']),
  });
  const [present, setPresent] = useState<Set<string>>(new Set(report?.attendees.map((a) => a.personId) ?? []));
  const [visitors, setVisitors] = useState<Visitor[]>(report?.visitors ?? []);
  const [followUps, setFollowUps] = useState<FollowUp[]>(report?.followUps ?? []);
  const [growth, setGrowth] = useState<Record<string, NextStep | ''>>(Object.fromEntries(report?.growth.map((g) => [g.personId, g.step]) ?? []));

  const save = useMutation({
    mutationFn: async (submit: boolean) => {
      const body = {
        ...form,
        topic: form.topic || null,
        materialId: form.materialId || null,
        notes: form.notes || null,
        highlights: form.highlights || null,
        prayerNeeds: form.prayerNeeds || null,
        attendeeIds: [...present],
        visitors,
        followUps,
        growth: Object.entries(growth)
          .filter(([, step]) => step)
          .map(([personId, step]) => ({ personId, step: step as NextStep })),
        submit,
      };
      return report
        ? unwrap(await api.PUT('/api/cells/{cellId}/reports/{reportId}', { params: { path: { cellId: cell.id, reportId: report.id } }, body }))
        : unwrap(await api.POST('/api/cells/{cellId}/reports', { params: { path: { cellId: cell.id } }, body }));
    },
    onSuccess: (saved) => {
      queryClient.setQueryData(['my-cell-report', saved.id], saved);
      void queryClient.invalidateQueries({ queryKey: ['my-cell-reports', cell.id] });
      if (saved.status === 'Submitted') navigate(`/my-cells/${cell.id}`);
      else if (!report) navigate(`/my-cells/${cell.id}/reports/${saved.id}`, { replace: true });
    },
  });
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [key]: e.target.value });
  const toggle = (personId: string) =>
    setPresent((p) => {
      const next = new Set(p);
      if (next.has(personId)) next.delete(personId);
      else next.add(personId);
      return next;
    });
  const editVisitor = (i: number, patch: Partial<Visitor>) => setVisitors((v) => v.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  const editFollowUp = (i: number, patch: Partial<FollowUp>) => setFollowUps((f) => f.map((x, j) => (j === i ? { ...x, ...patch } : x)));

  return (
    <>
      <PageHeader title={report ? `${cell.name}: draft report` : `${cell.name}: record a meeting`} subtitle="Pastors see this once you submit it." />
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate(true);
        }}
      >
        <div className="grid-2">
          <Card title="The meeting">
            <div className="stack">
              <Field label="Date">
                <TextInput type="date" value={form.meetingDate} max={today()} onChange={set('meetingDate')} required />
              </Field>
              <Field label="Lesson">
                <Select value={form.materialId} onChange={set('materialId')}>
                  <option value="">None of my lessons</option>
                  {materials.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.title}
                    </option>
                  ))}
                </Select>
              </Field>
              <Field label="Topic">
                <TextInput value={form.topic} onChange={set('topic')} maxLength={200} placeholder="What you discussed" />
              </Field>
              <Field label="How it went">
                <textarea className="input" rows={4} value={form.notes} onChange={set('notes')} />
              </Field>
              <Field label="Highlights and testimonies">
                <textarea className="input" rows={3} value={form.highlights} onChange={set('highlights')} />
              </Field>
            </div>
          </Card>
          <Card title={`Who came (${present.size + visitors.length})`}>
            <ul className="list">
              {cell.members.map((m) => (
                <li key={m.personId}>
                  <label className="checkbox">
                    <input type="checkbox" checked={present.has(m.personId)} onChange={() => toggle(m.personId)} />
                    {m.name}
                  </label>
                </li>
              ))}
            </ul>
            <h3 className="small">Visitors</h3>
            {visitors.map((v, i) => (
              <div key={i} className="review-item stack">
                <div className="row">
                  <TextInput placeholder="First name" value={v.firstName} onChange={(e) => editVisitor(i, { firstName: e.target.value })} required aria-label="Visitor first name" />
                  <TextInput placeholder="Last name" value={v.lastName ?? ''} onChange={(e) => editVisitor(i, { lastName: e.target.value || null })} aria-label="Visitor last name" />
                </div>
                <label className="checkbox">
                  <input type="checkbox" checked={v.agreedToBeContacted} onChange={(e) => editVisitor(i, { agreedToBeContacted: e.target.checked })} />
                  They agreed to the church contacting them
                </label>
                {v.agreedToBeContacted && (
                  <div className="row">
                    <TextInput placeholder="Mobile" value={v.mobile ?? ''} onChange={(e) => editVisitor(i, { mobile: e.target.value || null })} aria-label="Visitor mobile" />
                    <TextInput placeholder="Email" type="email" value={v.email ?? ''} onChange={(e) => editVisitor(i, { email: e.target.value || null })} aria-label="Visitor email" />
                  </div>
                )}
                <button type="button" className="link-button" onClick={() => setVisitors((x) => x.filter((_, j) => j !== i))}>
                  Remove visitor
                </button>
              </div>
            ))}
            <p className="small muted">Without their agreement only the first name is kept, for the count.</p>
            <Button onClick={() => setVisitors((v) => [...v, { firstName: '', lastName: null, mobile: null, email: null, agreedToBeContacted: false }])}>Add a visitor</Button>
          </Card>
          <Card title="Prayer and follow-ups">
            <div className="stack">
              <Field label="Prayer needs">
                <textarea className="input" rows={3} value={form.prayerNeeds} onChange={set('prayerNeeds')} />
              </Field>
              {followUps.map((f, i) => (
                <div key={f.id} className="review-item stack">
                  <Select
                    value={f.personId ?? ''}
                    onChange={(e) => {
                      const member = cell.members.find((m) => m.personId === e.target.value);
                      editFollowUp(i, { personId: member?.personId ?? null, name: member?.name ?? f.name });
                    }}
                    aria-label="Who"
                  >
                    <option value="">Someone else (type a name)</option>
                    {cell.members.map((m) => (
                      <option key={m.personId} value={m.personId}>
                        {m.name}
                      </option>
                    ))}
                  </Select>
                  {!f.personId && <TextInput placeholder="Name" value={f.name} onChange={(e) => editFollowUp(i, { name: e.target.value })} required aria-label="Name" />}
                  <textarea className="input" rows={2} placeholder="What's needed" value={f.note} onChange={(e) => editFollowUp(i, { note: e.target.value })} aria-label="What's needed" />
                  <label className="checkbox">
                    <input type="checkbox" checked={f.urgent} onChange={(e) => editFollowUp(i, { urgent: e.target.checked })} />
                    Urgent: a pastor should reach out this week
                  </label>
                  <button type="button" className="link-button" onClick={() => setFollowUps((x) => x.filter((_, j) => j !== i))}>
                    Remove
                  </button>
                </div>
              ))}
              <Button onClick={() => setFollowUps((f) => [...f, { id: crypto.randomUUID(), personId: null, name: '', note: '', urgent: false }])}>Add a follow-up</Button>
            </div>
          </Card>
          <Card title="Growth and next steps">
            <ul className="list">
              {cell.members
                .filter((m) => m.role === 'Member')
                .map((m) => (
                  <li key={m.personId} className="list-row">
                    <span>{m.name}</span>
                    <Select value={growth[m.personId] ?? ''} onChange={(e) => setGrowth({ ...growth, [m.personId]: e.target.value as NextStep | '' })} aria-label={`Next step for ${m.name}`}>
                      <option value="">—</option>
                      {(Object.keys(nextStepLabel) as NextStep[]).map((s) => (
                        <option key={s} value={s}>
                          Ready for {nextStepLabel[s].toLowerCase()}
                        </option>
                      ))}
                    </Select>
                  </li>
                ))}
            </ul>
            <Field label="Is the cell ready to multiply?">
              <Select value={form.multiplication} onChange={set('multiplication')}>
                {(Object.keys(multiplicationLabel) as Schemas['MultiplicationReadiness'][]).map((k) => (
                  <option key={k} value={k}>
                    {multiplicationLabel[k]}
                  </option>
                ))}
              </Select>
            </Field>
          </Card>
        </div>
        <ErrorNote error={save.error} />
        <div className="row">
          <Button variant="primary" type="submit" busy={save.isPending && save.variables}>
            Submit to pastors
          </Button>
          <Button onClick={() => save.mutate(false)} busy={save.isPending && !save.variables}>
            Save draft
          </Button>
        </div>
      </form>
    </>
  );
}

/** Pastors: what every leader is teaching. */
export function CellMaterialsPage() {
  const materials = useQuery({ queryKey: ['cell-materials'], queryFn: async () => unwrap(await api.GET('/api/admin/cells/materials')) });
  return (
    <>
      <PageHeader title="Teaching materials" subtitle="What each cell leader has prepared, newest first." />
      <ErrorNote error={materials.error} />
      {materials.isPending ? (
        <Loading />
      ) : materials.data?.length === 0 ? (
        <Card>
          <Empty>No materials yet.</Empty>
        </Card>
      ) : (
        <div className="stack">
          {materials.data?.map((m) => (
            <MaterialCard key={m.id} material={m} />
          ))}
        </div>
      )}
    </>
  );
}

export function MaterialCard({ material: m, actions }: { material: Schemas['MaterialDto']; actions?: React.ReactNode }) {
  return (
    <Card
      title={
        <>
          {m.title} {m.sharedWithMembers ? <Badge tone="success">Shared with members</Badge> : <Badge>Leaders and pastors</Badge>}
        </>
      }
      actions={actions}
    >
      <p className="small muted">
        {m.cellName} · {m.writtenBy} · updated {formatDateTime(m.updatedAt)}
        {m.forDate && ` · for ${formatDate(m.forDate)}`}
      </p>
      {m.body && <p className="quote">{m.body}</p>}
      {m.link && (
        <a href={m.link} target="_blank" rel="noopener noreferrer">
          {m.link}
        </a>
      )}
    </Card>
  );
}
