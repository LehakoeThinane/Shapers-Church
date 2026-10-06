import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { duration, emptyItem, hhmm, longDay, positionsOf, shortDay, statusLabel, statusTone, useSongs, useTeams, type PlanItem, type PlanItemKind } from '../lib/services';

type Plan = Schemas['PlanDto'];

const today = () => new Date(Date.now() + 2 * 3600_000).toISOString().slice(0, 10);

/** Planners and schedulers: the coming services and how full their teams are. */
export function ServicePlansPage() {
  const { data: access } = useAccess();
  const navigate = useNavigate();
  const plans = useQuery({ queryKey: ['services', 'plans'], queryFn: async () => unwrap(await api.GET('/api/admin/services/plans')) });
  const types = useQuery({ queryKey: ['services', 'types'], queryFn: async () => unwrap(await api.GET('/api/admin/services/types')) });
  const [form, setForm] = useState({ typeId: '', date: '', title: '' });
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/services/plans', {
          body: { serviceTypeId: form.typeId || null, date: form.date, title: form.title || null, startTime: null, scope: null },
        }),
      ),
    onSuccess: (p) => navigate(`/services/plans/${p.id}`),
  });
  const canPlan = can(access, Permissions.servicesPlans);

  return (
    <>
      <PageHeader title="Services" subtitle="Orders of service, the teams serving, and who has said yes." />
      {canPlan && (
        <Card title="New plan">
          <form
            className="form-grid"
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate();
            }}
          >
            <Field label="From the template">
              <Select value={form.typeId} onChange={(e) => setForm({ ...form, typeId: e.target.value })}>
                <option value="">Blank plan</option>
                {types.data?.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Date">
              <TextInput type="date" required min={today()} value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} />
            </Field>
            <Field label="Title (optional)">
              <TextInput value={form.title} maxLength={120} placeholder="e.g. Easter Sunday" onChange={(e) => setForm({ ...form, title: e.target.value })} />
            </Field>
            <div className="form-actions">
              <ErrorNote error={create.error} />
              <Button variant="primary" type="submit" busy={create.isPending}>
                Create plan
              </Button>
            </div>
          </form>
        </Card>
      )}
      <Card>
        <ErrorNote error={plans.error} />
        {plans.isPending ? (
          <Loading />
        ) : plans.data?.length === 0 ? (
          <Empty>No services planned yet. {canPlan ? 'Create one above, or set up a template first.' : ''}</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Service</th>
                <th>When</th>
                <th>Team</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {plans.data?.map((p) => (
                <tr key={p.id}>
                  <td>
                    <Link to={`/services/plans/${p.id}`}>{p.title}</Link>
                    {p.seriesTitle && <span className="small muted"> · {p.seriesTitle}</span>}
                  </td>
                  <td className="nowrap">
                    {longDay(p.date)} · {hhmm(p.startTime)}
                  </td>
                  <td className="small">
                    {p.accepted} of {p.needed} confirmed
                    {p.pending > 0 && ` · ${p.pending} asked`}
                    {p.declined > 0 && <span className="danger-text"> · {p.declined} can't</span>}
                  </td>
                  <td>{p.isLive && <Badge tone="accent">Live</Badge>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}

/** One service: details, the order of service, and the people serving. */
export function ServicePlanPage() {
  const { id = '' } = useParams();
  const plan = useQuery({ queryKey: ['services', 'plan', id], queryFn: async () => unwrap(await api.GET('/api/admin/services/plans/{id}', { params: { path: { id } } })) });
  if (plan.isPending) return <Loading />;
  if (!plan.data) return <ErrorNote error={plan.error} />;
  return <PlanEditor key={plan.data.updatedAt} plan={plan.data} />;
}

function PlanEditor({ plan }: { plan: Plan }) {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const canPlan = can(access, Permissions.servicesPlans);
  const saved = (p: Plan) => {
    queryClient.setQueryData(['services', 'plan', p.id], p);
    void queryClient.invalidateQueries({ queryKey: ['services', 'plans'] });
  };
  const [details, setDetails] = useState({
    title: plan.title,
    date: plan.date,
    startTime: hhmm(plan.startTime),
    seriesTitle: plan.seriesTitle ?? '',
    notes: plan.notes ?? '',
  });
  const saveDetails = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT('/api/admin/services/plans/{id}', {
          params: { path: { id: plan.id } },
          body: { title: details.title, date: details.date, startTime: `${details.startTime}:00`, seriesTitle: details.seriesTitle || null, notes: details.notes || null, livestreamId: plan.livestreamId },
        }),
      ),
    onSuccess: saved,
  });
  const remove = useMutation({
    mutationFn: async () => unwrap(await api.DELETE('/api/admin/services/plans/{id}', { params: { path: { id: plan.id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['services', 'plans'] });
      navigate('/services');
    },
  });

  return (
    <>
      <PageHeader
        title={plan.title}
        subtitle={`${longDay(plan.date)} · ${hhmm(plan.startTime)}–${hhmm(plan.endTime)}`}
        actions={
          <span className="row">
            <Link className="btn btn-secondary" to={`/services/plans/${plan.id}/live`}>
              Live run sheet
            </Link>
            <Link className="btn btn-secondary" to={`/services/plans/${plan.id}/stand`}>
              Music stand
            </Link>
            <Button onClick={() => window.print()}>Print</Button>
          </span>
        }
      />
      <div className="grid-2">
        <div className="stack">
          <OrderOfServiceCard plan={plan} editable={canPlan} onSaved={saved} />
          {canPlan && (
            <Card title="Details">
              <form
                className="stack"
                onSubmit={(e) => {
                  e.preventDefault();
                  saveDetails.mutate();
                }}
              >
                <div className="form-grid">
                  <Field label="Title">
                    <TextInput required maxLength={120} value={details.title} onChange={(e) => setDetails({ ...details, title: e.target.value })} />
                  </Field>
                  <Field label="Date">
                    <TextInput type="date" required value={details.date} onChange={(e) => setDetails({ ...details, date: e.target.value })} />
                  </Field>
                  <Field label="Starts at">
                    <TextInput type="time" required value={details.startTime} onChange={(e) => setDetails({ ...details, startTime: e.target.value })} />
                  </Field>
                  <Field label="Series (optional)">
                    <TextInput maxLength={120} value={details.seriesTitle} onChange={(e) => setDetails({ ...details, seriesTitle: e.target.value })} />
                  </Field>
                </div>
                <Field label="Notes for everyone serving" hint="Arrival times, dress, parking: shown in the app.">
                  <textarea className="input" rows={3} maxLength={4000} value={details.notes} onChange={(e) => setDetails({ ...details, notes: e.target.value })} />
                </Field>
                <ErrorNote error={saveDetails.error ?? remove.error} />
                <div className="row">
                  <Button variant="primary" type="submit" busy={saveDetails.isPending}>
                    Save details
                  </Button>
                  <Button variant="ghost" onClick={() => window.confirm('Delete this plan and everyone scheduled on it?') && remove.mutate()}>
                    Delete plan
                  </Button>
                </div>
              </form>
            </Card>
          )}
        </div>
        <TeamCard plan={plan} onSaved={saved} />
      </div>
    </>
  );
}

/** The order of service, editable in place. Also used for templates. */
export function OrderEditor({ items, onChange, startTime }: { items: PlanItem[]; onChange: (items: PlanItem[]) => void; startTime: string }) {
  const songs = useSongs();
  const update = (i: number, patch: Partial<PlanItem>) => onChange(items.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  const move = (i: number, by: number) => {
    const next = [...items];
    const [item] = next.splice(i, 1);
    if (!item) return;
    next.splice(Math.max(0, Math.min(next.length, i + by)), 0, item);
    onChange(next);
  };
  // Each item's start, and the end of the service, from the start time and the lengths before it.
  const starts = items.reduce<number[]>((acc, item, i) => [...acc, (acc[i] ?? 0) + item.lengthSeconds / 60], [toMinutes(startTime)]);

  return (
    <div className="stack">
      <table className="table order-table">
        <thead>
          <tr>
            <th>Time</th>
            <th>Item</th>
            <th>Length</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {items.map((item, i) => {
            const song = songs.data?.find((s) => s.id === item.songId);
            return (
              <tr key={i} className={item.kind === 'Header' ? 'order-header' : undefined}>
                <td className="small muted nowrap">{item.kind === 'Header' ? '' : fromMinutes(starts[i] ?? 0)}</td>
                <td>
                  <div className="stack-tight">
                    {item.kind === 'Song' ? (
                      <div className="row">
                        <Select
                          value={item.songId ?? ''}
                          aria-label="Song"
                          onChange={(e) => {
                            const chosen = songs.data?.find((s) => s.id === e.target.value);
                            update(i, { songId: e.target.value || null, title: chosen?.title ?? '', key: item.key ?? chosen?.keys[0] ?? null, arrangementId: null });
                          }}
                        >
                          <option value="">Choose a song</option>
                          {songs.data?.map((s) => (
                            <option key={s.id} value={s.id}>
                              {s.title}
                              {s.lastUsed ? ` (last ${shortDay(s.lastUsed)})` : ''}
                            </option>
                          ))}
                        </Select>
                        <TextInput className="input key-input" aria-label="Key" placeholder="Key" maxLength={10} value={item.key ?? ''} onChange={(e) => update(i, { key: e.target.value || null })} />
                      </div>
                    ) : (
                      <TextInput aria-label="Title" required maxLength={120} value={item.title} placeholder={item.kind === 'Header' ? 'Section, e.g. Worship' : 'e.g. Welcome'} onChange={(e) => update(i, { title: e.target.value })} />
                    )}
                    {item.kind !== 'Header' && (
                      <div className="row">
                        <TextInput aria-label="Who leads" placeholder="Who leads (optional)" maxLength={80} value={item.leader ?? ''} onChange={(e) => update(i, { leader: e.target.value || null })} />
                        <TextInput aria-label="Notes" placeholder="Notes (optional)" maxLength={2000} value={item.description ?? ''} onChange={(e) => update(i, { description: e.target.value || null })} />
                      </div>
                    )}
                    {song && song.timesUsed > 0 && <span className="small muted">Used {song.timesUsed} times</span>}
                  </div>
                </td>
                <td>
                  {item.kind !== 'Header' && (
                    <TextInput
                      className="input minutes-input"
                      type="number"
                      min={0}
                      max={240}
                      aria-label="Minutes"
                      value={Math.round(item.lengthSeconds / 60)}
                      onChange={(e) => update(i, { lengthSeconds: Number(e.target.value || 0) * 60 })}
                    />
                  )}
                </td>
                <td className="nowrap">
                  <button type="button" className="link-button" aria-label="Move up" onClick={() => move(i, -1)} disabled={i === 0}>
                    ↑
                  </button>
                  <button type="button" className="link-button" aria-label="Move down" onClick={() => move(i, 1)} disabled={i === items.length - 1}>
                    ↓
                  </button>
                  <button type="button" className="link-button" onClick={() => onChange(items.filter((_, j) => j !== i))}>
                    Remove
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="row">
        {(['Header', 'Item', 'Song'] as PlanItemKind[]).map((kind) => (
          <Button key={kind} onClick={() => onChange([...items, emptyItem(kind)])}>
            Add {kind === 'Header' ? 'a section' : kind === 'Song' ? 'a song' : 'an item'}
          </Button>
        ))}
        <span className="small muted">Ends {fromMinutes(starts[items.length] ?? 0)}</span>
      </div>
    </div>
  );
}

const toMinutes = (time: string) => {
  const [h = 0, m = 0] = time.split(':').map(Number);
  return h * 60 + m;
};
const fromMinutes = (minutes: number) => `${String(Math.floor(minutes / 60) % 24).padStart(2, '0')}:${String(Math.round(minutes % 60)).padStart(2, '0')}`;

function OrderOfServiceCard({ plan, editable, onSaved }: { plan: Plan; editable: boolean; onSaved: (p: Plan) => void }) {
  const [items, setItems] = useState(plan.items.map((i) => i.item));
  const dirty = JSON.stringify(items) !== JSON.stringify(plan.items.map((i) => i.item));
  const save = useMutation({
    mutationFn: async () => unwrap(await api.PUT('/api/admin/services/plans/{id}/items', { params: { path: { id: plan.id } }, body: { items } })),
    onSuccess: onSaved,
  });

  if (!editable) {
    return (
      <Card title="Order of service">
        <ReadOnlyOrder items={plan.items} />
      </Card>
    );
  }

  return (
    <Card title="Order of service" actions={dirty && <Badge tone="accent">Not saved</Badge>}>
      <OrderEditor items={items} onChange={setItems} startTime={plan.startTime} />
      <ErrorNote error={save.error} />
      <Button variant="primary" busy={save.isPending} disabled={!dirty} onClick={() => save.mutate()}>
        Save order of service
      </Button>
    </Card>
  );
}

export function ReadOnlyOrder({ items, current }: { items: Schemas['PlanItemDto'][]; current?: string | null }) {
  if (items.length === 0) return <Empty>No items yet.</Empty>;
  return (
    <table className="table order-table">
      <tbody>
        {items.map(({ item, startsAt, songTitle }) =>
          item.kind === 'Header' ? (
            <tr key={item.id} className="order-header">
              <td colSpan={3}>{item.title}</td>
            </tr>
          ) : (
            <tr key={item.id} className={item.id === current ? 'order-current' : undefined}>
              <td className="small muted nowrap">{hhmm(startsAt)}</td>
              <td>
                <strong>{songTitle ?? item.title}</strong>
                {item.key && <Badge>{item.key}</Badge>}
                {item.leader && <span className="small muted"> · {item.leader}</span>}
                {item.description && <div className="small muted">{item.description}</div>}
              </td>
              <td className="small nowrap">{duration(item.lengthSeconds)}</td>
            </tr>
          ),
        )}
      </tbody>
    </table>
  );
}

/** Who's serving: needs per position, people asked and their answers, and scheduling more. */
function TeamCard({ plan, onSaved }: { plan: Plan; onSaved: (p: Plan) => void }) {
  const { data: access } = useAccess();
  const teams = useTeams();
  const canSchedule = can(access, Permissions.servicesSchedule);
  const canPlan = can(access, Permissions.servicesPlans);
  const [picking, setPicking] = useState<string | null>(null);
  const [addPosition, setAddPosition] = useState('');
  const unschedule = useMutation({
    mutationFn: async (assignmentId: string) => unwrap(await api.DELETE('/api/admin/services/assignments/{id}', { params: { path: { id: assignmentId } } })),
    onSuccess: onSaved,
  });
  const setNeed = useMutation({
    mutationFn: async ({ positionId, count }: { positionId: string; count: number }) => {
      const needs = plan.needs.filter((n) => n.needed > 0 && n.positionId !== positionId).map((n) => ({ positionId: n.positionId, count: n.needed }));
      if (count > 0) needs.push({ positionId, count });
      return unwrap(await api.PUT('/api/admin/services/plans/{id}/needs', { params: { path: { id: plan.id } }, body: { needs } }));
    },
    onSuccess: onSaved,
  });

  const byTeam = new Map<string, Schemas['NeedDto'][]>();
  plan.needs.forEach((n) => byTeam.set(n.team, [...(byTeam.get(n.team) ?? []), n]));
  const unused = positionsOf(teams.data).filter((p) => !plan.needs.some((n) => n.positionId === p.id));

  return (
    <Card title="Team">
      <ErrorNote error={unschedule.error ?? setNeed.error} />
      {plan.needs.length === 0 && <Empty>No positions on this plan yet.</Empty>}
      {[...byTeam.entries()].map(([team, needs]) => (
        <div key={team} className="stack-tight team-block">
          <h3 className="small">{team}</h3>
          {needs.map((n) => {
            const people = plan.assignments.filter((a) => a.positionId === n.positionId);
            const short = n.needed - n.filled - n.pending;
            return (
              <div key={n.positionId} className="review-item stack-tight">
                <div className="list-row">
                  <strong>{n.position}</strong>
                  <span className="small">
                    {n.filled}/{n.needed} confirmed {short > 0 && <Badge tone="danger">{short} needed</Badge>}
                    {canPlan && (
                      <span className="row">
                        <button type="button" className="link-button" aria-label="Need one fewer" onClick={() => setNeed.mutate({ positionId: n.positionId, count: Math.max(0, n.needed - 1) })}>
                          −
                        </button>
                        <button type="button" className="link-button" aria-label="Need one more" onClick={() => setNeed.mutate({ positionId: n.positionId, count: n.needed + 1 })}>
                          +
                        </button>
                      </span>
                    )}
                  </span>
                </div>
                {people.map((a) => (
                  <div key={a.id} className="list-row">
                    <span>
                      {a.name} <Badge tone={statusTone[a.status]}>{statusLabel[a.status]}</Badge>
                      {a.declineReason && <span className="small muted"> “{a.declineReason}”</span>}
                    </span>
                    {canSchedule && (
                      <button type="button" className="link-button" onClick={() => unschedule.mutate(a.id)}>
                        Remove
                      </button>
                    )}
                  </div>
                ))}
                {canSchedule &&
                  (picking === n.positionId ? (
                    <Candidates plan={plan} positionId={n.positionId} onDone={(p) => (p ? onSaved(p) : setPicking(null))} />
                  ) : (
                    <button type="button" className="link-button" onClick={() => setPicking(n.positionId)}>
                      Schedule someone
                    </button>
                  ))}
              </div>
            );
          })}
        </div>
      ))}
      {canPlan && unused.length > 0 && (
        <div className="row">
          <Select value={addPosition} onChange={(e) => setAddPosition(e.target.value)} aria-label="Add a position">
            <option value="">Add a position…</option>
            {unused.map((p) => (
              <option key={p.id} value={p.id}>
                {p.label}
              </option>
            ))}
          </Select>
          <Button disabled={!addPosition} busy={setNeed.isPending} onClick={() => setNeed.mutate({ positionId: addPosition, count: 1 })}>
            Add
          </Button>
        </div>
      )}
      {teams.data?.length === 0 && (
        <p className="small muted">
          Set up <Link to="/services/teams">teams and positions</Link> first.
        </p>
      )}
    </Card>
  );
}

function Candidates({ plan, positionId, onDone }: { plan: Plan; positionId: string; onDone: (p?: Plan) => void }) {
  const candidates = useQuery({
    queryKey: ['services', 'candidates', plan.id, positionId],
    queryFn: async () => unwrap(await api.GET('/api/admin/services/plans/{id}/candidates', { params: { path: { id: plan.id }, query: { positionId } } })),
  });
  const assign = useMutation({
    mutationFn: async (personId: string) => unwrap(await api.POST('/api/admin/services/plans/{id}/assignments', { params: { path: { id: plan.id } }, body: { positionId, personId } })),
    onSuccess: (p) => onDone(p),
  });
  return (
    <div className="ai-suggestion stack-tight">
      {candidates.isPending && <Loading />}
      {candidates.data?.length === 0 && <span className="small muted">No one on the team plays this position yet. Add them under Teams.</span>}
      {candidates.data?.map((c) => (
        <div key={c.personId} className="list-row">
          <span>
            {c.name}
            <span className="small muted"> {c.reason ?? (c.lastServed ? `last served ${shortDay(c.lastServed)}` : 'not served yet')}</span>
          </span>
          {c.available && (
            <button type="button" className="link-button" disabled={assign.isPending} onClick={() => assign.mutate(c.personId)}>
              Ask
            </button>
          )}
        </div>
      ))}
      <ErrorNote error={candidates.error ?? assign.error} />
      <button type="button" className="link-button" onClick={() => onDone()}>
        Close
      </button>
    </div>
  );
}

/** The coming weeks side by side, every position, so gaps stand out. */
export function ServingMatrixPage() {
  const [weeks, setWeeks] = useState(6);
  const matrix = useQuery({
    queryKey: ['services', 'matrix', weeks],
    queryFn: async () => unwrap(await api.GET('/api/admin/services/matrix', { params: { query: { weeks } } })),
  });
  const m = matrix.data;
  return (
    <>
      <PageHeader
        title="Matrix"
        subtitle="Every position across the coming services. Red cells still need someone."
        actions={
          <Select value={weeks} onChange={(e) => setWeeks(Number(e.target.value))} aria-label="Weeks">
            {[4, 6, 8, 12].map((w) => (
              <option key={w} value={w}>
                {w} weeks
              </option>
            ))}
          </Select>
        }
      />
      <Card>
        <ErrorNote error={matrix.error} />
        {matrix.isPending ? (
          <Loading />
        ) : !m || m.plans.length === 0 ? (
          <Empty>No services in this period.</Empty>
        ) : (
          <div className="matrix-scroll">
            <table className="table matrix">
              <thead>
                <tr>
                  <th>Position</th>
                  {m.plans.map((p) => (
                    <th key={p.id}>
                      <Link to={`/services/plans/${p.id}`}>{shortDay(p.date)}</Link>
                      <div className="small muted">{p.title}</div>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {m.rows.map((r) => (
                  <tr key={r.positionId}>
                    <th className="small">
                      {r.team}: {r.position}
                    </th>
                    {m.plans.map((p) => {
                      const cell = m.cells.find((c) => c.planId === p.id && c.positionId === r.positionId);
                      const confirmed = cell?.people.filter((x) => x.status === 'Accepted').length ?? 0;
                      const asked = cell?.people.filter((x) => x.status === 'Pending').length ?? 0;
                      const gap = (cell?.needed ?? 0) > confirmed + asked;
                      return (
                        <td key={p.id} className={gap ? 'matrix-gap' : undefined}>
                          {cell?.people.map((x) => (
                            <div key={x.assignmentId} className="small">
                              {x.name} <Badge tone={statusTone[x.status]}>{statusLabel[x.status]}</Badge>
                            </div>
                          ))}
                          {gap && <span className="small danger-text">Needs {(cell?.needed ?? 0) - confirmed - asked}</span>}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  );
}

/** During the service: what's on now, for how long, and what's next. Refreshes every two seconds. */
export function LiveRunSheetPage() {
  const { id = '' } = useParams();
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const live = useQuery({
    queryKey: ['services', 'live', id],
    queryFn: async () => unwrap(await api.GET('/api/services/plans/{id}/live', { params: { path: { id } } })),
    refetchInterval: 2000,
  });
  const control = useMutation({
    mutationFn: async ({ action, itemId }: { action: 'start' | 'next' | 'previous' | 'goto' | 'end'; itemId?: string }) =>
      unwrap(await api.POST('/api/admin/services/plans/{id}/live/{action}', { params: { path: { id, action } }, body: { itemId: itemId ?? null } })),
    onSuccess: (data) => queryClient.setQueryData(['services', 'live', id], data),
  });
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 500);
    return () => clearInterval(t);
  }, []);

  if (live.isPending) return <Loading />;
  if (!live.data) return <ErrorNote error={live.error} />;
  const l = live.data;
  const timed = l.items.filter((i) => i.item.kind !== 'Header');
  const index = timed.findIndex((i) => i.item.id === l.currentItemId);
  const current = index >= 0 ? timed[index] : null;
  const next = index >= 0 ? timed[index + 1] : timed[0];
  // Clock difference between this device and the server, so every screen counts the same.
  const skew = new Date(l.serverTime).getTime() - (live.dataUpdatedAt || now);
  const elapsed = current && l.currentStartedAt ? Math.max(0, (now + skew - new Date(l.currentStartedAt).getTime()) / 1000) : 0;
  const remaining = current ? current.item.lengthSeconds - elapsed : 0;
  const controls = can(access, Permissions.servicesPlans);

  return (
    <>
      <PageHeader title={`Live: ${l.title}`} subtitle={<Link to={`/services/plans/${id}`}>Back to the plan</Link>} />
      <div className="grid-2">
        <Card title={l.isLive ? 'Now' : 'Not started'}>
          {current ? (
            <div className="live-now stack">
              <span className="live-title">{current.songTitle ?? current.item.title}</span>
              {current.item.key && <Badge>{current.item.key}</Badge>}
              {current.item.leader && <span className="muted">{current.item.leader}</span>}
              <span className={`live-clock${remaining < 0 ? ' danger-text' : ''}`}>
                {remaining < 0 ? '+' : ''}
                {clock(Math.abs(remaining))}
              </span>
              <span className="small muted">
                {clock(elapsed)} of {duration(current.item.lengthSeconds)}
              </span>
              {current.item.description && <p className="quote">{current.item.description}</p>}
            </div>
          ) : (
            <p className="muted">{l.isLive ? '' : 'The run sheet starts when the producer presses Start.'}</p>
          )}
          {next && (
            <p className="small">
              <strong>Next:</strong> {next.songTitle ?? next.item.title} ({duration(next.item.lengthSeconds)})
            </p>
          )}
          {controls && (
            <div className="row">
              {!l.isLive ? (
                <Button variant="primary" busy={control.isPending} onClick={() => control.mutate({ action: 'start' })}>
                  Start
                </Button>
              ) : (
                <>
                  <Button onClick={() => control.mutate({ action: 'previous' })}>Previous</Button>
                  <Button variant="primary" onClick={() => control.mutate({ action: 'next' })}>
                    Next
                  </Button>
                  <Button variant="ghost" onClick={() => control.mutate({ action: 'end' })}>
                    End
                  </Button>
                </>
              )}
            </div>
          )}
          <ErrorNote error={control.error} />
        </Card>
        <Card title="Order of service">
          <ReadOnlyOrder items={l.items} current={l.currentItemId} />
          {controls && l.isLive && (
            <p className="small muted jump-links">
              Jump to:
              {timed.map((i) => (
                <button key={i.item.id} type="button" className="link-button" onClick={() => control.mutate({ action: 'goto', itemId: i.item.id })}>
                  {i.songTitle ?? i.item.title}
                </button>
              ))}
            </p>
          )}
        </Card>
      </div>
    </>
  );
}

const clock = (seconds: number) => `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;

/** Full-screen chord charts for a tablet on the stand, in the plan's order. */
export function MusicStandPage() {
  const { id = '' } = useParams();
  const plan = useQuery({ queryKey: ['services', 'rehearse', id], queryFn: async () => unwrap(await api.GET('/api/services/plans/{id}/rehearse', { params: { path: { id } } })) });
  const [index, setIndex] = useState(0);
  const [lyrics, setLyrics] = useState(false);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight' || e.key === 'PageDown') setIndex((i) => i + 1);
      if (e.key === 'ArrowLeft' || e.key === 'PageUp') setIndex((i) => Math.max(0, i - 1));
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  if (plan.isPending) return <Loading />;
  if (!plan.data) return <ErrorNote error={plan.error} />;
  const songs = plan.data.songs;
  if (songs.length === 0) {
    return (
      <>
        <PageHeader title="Music stand" subtitle={<Link to={`/services/plans/${id}`}>Back to the plan</Link>} />
        <Card>
          <Empty>This plan has no songs yet.</Empty>
        </Card>
      </>
    );
  }
  const song = songs[Math.min(index, songs.length - 1)]!;
  const isPdf = song.chartUrl?.toLowerCase().includes('.pdf');

  return (
    <div className="stand">
      <div className="stand-bar">
        <Link to={`/services/plans/${id}`}>← Plan</Link>
        <strong>
          {song.title} {song.key && <Badge>{song.key}</Badge>} {song.bpm && <span className="small muted">{song.bpm} bpm</span>}
        </strong>
        <span className="row">
          <Button onClick={() => setIndex(Math.max(0, index - 1))} disabled={index === 0}>
            ← {songs[index - 1]?.title ?? ''}
          </Button>
          <Button onClick={() => setIndex(Math.min(songs.length - 1, index + 1))} disabled={index >= songs.length - 1}>
            {songs[index + 1]?.title ?? ''} →
          </Button>
          {song.lyrics && <Button onClick={() => setLyrics(!lyrics)}>{lyrics ? 'Chart' : 'Lyrics'}</Button>}
          <Button onClick={() => void document.documentElement.requestFullscreen?.()}>Full screen</Button>
        </span>
      </div>
      {lyrics || !song.chartUrl ? (
        <pre className="stand-lyrics">{song.lyrics ?? 'No chart or lyrics for this song yet.'}</pre>
      ) : isPdf ? (
        <iframe className="stand-chart" src={song.chartUrl} title={`${song.title} chart`} />
      ) : (
        <img className="stand-chart" src={song.chartUrl} alt={`${song.title} chart`} />
      )}
    </div>
  );
}
