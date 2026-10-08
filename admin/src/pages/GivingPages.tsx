import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDate, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { rands } from '../lib/money';

type Method = Schemas['GiftMethod'];

const methodLabel: Record<Method, string> = { Card: 'Card', Eft: 'EFT', Cash: 'Cash' };

const thisMonth = () => new Date().toISOString().slice(0, 7);

const monthName = (month: string) =>
  new Intl.DateTimeFormat('en-ZA', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${month}-01T00:00:00Z`));

function shiftMonth(month: string, by: number) {
  const d = new Date(`${month}-01T00:00:00Z`);
  d.setUTCMonth(d.getUTCMonth() + by);
  return d.toISOString().slice(0, 7);
}

/** This month's giving at a glance: the total, by fund and by method, and the latest gifts. */
export function GivingOverviewPage() {
  const [current] = useState(thisMonth);
  const [month, setMonth] = useState(current);
  const overview = useQuery({
    queryKey: ['giving', 'overview', month],
    queryFn: async () => unwrap(await api.GET('/api/admin/giving/overview', { params: { query: { month } } })),
  });

  return (
    <>
      <PageHeader
        title="Giving"
        subtitle="Gifts received by card online, and by EFT and cash as the finance team records them."
        actions={
          <div className="row">
            <Button variant="ghost" aria-label="Previous month" onClick={() => setMonth(shiftMonth(month, -1))}>
              ‹
            </Button>
            <strong className="nowrap">{monthName(month)}</strong>
            <Button variant="ghost" aria-label="Next month" disabled={month >= current} onClick={() => setMonth(shiftMonth(month, 1))}>
              ›
            </Button>
          </div>
        }
      />
      {overview.isPending && <Loading />}
      <ErrorNote error={overview.error} />
      {overview.data && (
        <>
          <div className="giving-totals">
            <Card>
              <span className="small muted">Received in {monthName(overview.data.month)}</span>
              <span className="giving-big">{rands(overview.data.totalCents)}</span>
              <span className="small muted">
                {overview.data.count} {overview.data.count === 1 ? 'gift' : 'gifts'}
              </span>
            </Card>
            <Card title="By fund">
              <Totals rows={overview.data.byFund} />
            </Card>
            <Card title="By method">
              <Totals rows={overview.data.byMethod} />
            </Card>
          </div>
          <Card title="Latest gifts" actions={<Link to="/giving/gifts">All gifts</Link>}>
            <GiftTable gifts={overview.data.latest} />
          </Card>
        </>
      )}
    </>
  );
}

function Totals({ rows }: { rows: Schemas['TotalDto'][] }) {
  if (rows.length === 0) return <Empty>Nothing yet this month.</Empty>;
  return (
    <ul className="list">
      {rows.map((r) => (
        <li key={r.label} className="list-row">
          <span>{r.label}</span>
          <strong>{rands(r.amountCents)}</strong>
        </li>
      ))}
    </ul>
  );
}

function GiftTable({ gifts }: { gifts: Schemas['GiftDto'][] }) {
  const [year] = useState(() => new Date().getFullYear());
  if (gifts.length === 0) return <Empty>No gifts to show.</Empty>;
  return (
    <table className="table">
      <thead>
        <tr>
          <th>Date</th>
          <th>Giver</th>
          <th>Fund</th>
          <th>Method</th>
          <th className="giving-amount">Amount</th>
        </tr>
      </thead>
      <tbody>
        {gifts.map((g) => (
          <tr key={g.id}>
            <td className="nowrap">{formatDate(g.givenOn)}</td>
            <td>
              {g.personId ? <Link to={`/giving/statement/${g.personId}?year=${year}`}>{g.giverName}</Link> : g.giverName}
              {g.note && <span className="small muted"> · {g.note}</span>}
            </td>
            <td>{g.fundName}</td>
            <td>
              <Badge>{methodLabel[g.method]}</Badge>
            </td>
            <td className="giving-amount">{rands(g.amountCents)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Every gift received, with filters, and recording EFT and cash gifts. */
export function GiftsPage() {
  const { data: access } = useAccess();
  const [filters, setFilters] = useState({ from: '', to: '', fundId: '', method: '' as Method | '', search: '' });
  const [page, setPage] = useState(1);
  const [recording, setRecording] = useState(false);
  const funds = useFunds();
  const gifts = useQuery({
    queryKey: ['giving', 'gifts', filters, page],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/admin/giving/gifts', {
          params: {
            query: {
              from: filters.from || undefined,
              to: filters.to || undefined,
              fundId: filters.fundId || undefined,
              method: filters.method || undefined,
              search: filters.search || undefined,
              page,
            },
          },
        }),
      ),
  });
  const set = (change: Partial<typeof filters>) => {
    setFilters({ ...filters, ...change });
    setPage(1);
  };

  return (
    <>
      <PageHeader
        title="Gifts"
        subtitle="Card gifts arrive on their own. Record bank transfers and cash as they come in."
        actions={can(access, Permissions.givingManage) && !recording && <Button variant="primary" onClick={() => setRecording(true)}>Record a gift</Button>}
      />
      {recording && <RecordGift onDone={() => setRecording(false)} />}
      <Card>
        <div className="filters">
          <Field label="From">
            <TextInput type="date" value={filters.from} onChange={(e) => set({ from: e.target.value })} />
          </Field>
          <Field label="To">
            <TextInput type="date" value={filters.to} onChange={(e) => set({ to: e.target.value })} />
          </Field>
          <Field label="Fund">
            <Select value={filters.fundId} onChange={(e) => set({ fundId: e.target.value })}>
              <option value="">All funds</option>
              {funds.data?.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.name}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Method">
            <Select value={filters.method} onChange={(e) => set({ method: e.target.value as Method | '' })}>
              <option value="">All</option>
              <option value="Card">Card</option>
              <option value="Eft">EFT</option>
              <option value="Cash">Cash</option>
            </Select>
          </Field>
          <Field label="Giver">
            <TextInput type="search" placeholder="Name" value={filters.search} onChange={(e) => set({ search: e.target.value })} />
          </Field>
        </div>
        {gifts.isPending && <Loading />}
        <ErrorNote error={gifts.error} />
        {gifts.data && (
          <>
            <p className="small muted">
              {gifts.data.total} {gifts.data.total === 1 ? 'gift' : 'gifts'}, {rands(gifts.data.totalCents)} in all.
            </p>
            <GiftTable gifts={gifts.data.items} />
            {gifts.data.total > gifts.data.pageSize && (
              <div className="pager">
                <Button variant="ghost" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                  Newer
                </Button>
                <span className="small muted">Page {page}</span>
                <Button variant="ghost" disabled={page * gifts.data.pageSize >= gifts.data.total} onClick={() => setPage(page + 1)}>
                  Older
                </Button>
              </div>
            )}
          </>
        )}
      </Card>
    </>
  );
}

function useFunds() {
  return useQuery({ queryKey: ['giving', 'funds'], queryFn: async () => unwrap(await api.GET('/api/admin/giving/funds')) });
}

/** An EFT or cash gift. Choose the person when they have a church record; otherwise type the name on the statement. */
function RecordGift({ onDone }: { onDone: () => void }) {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const funds = useFunds();
  const [today] = useState(() => new Date().toISOString().slice(0, 10));
  const [form, setForm] = useState({ fundId: '', amount: '', method: 'Eft' as Method, givenOn: today, giverName: '', note: '' });
  const [person, setPerson] = useState<{ id: string; name: string } | null>(null);
  const [search, setSearch] = useState('');
  const canSearchPeople = can(access, Permissions.peopleView);
  const matches = useQuery({
    queryKey: ['giving', 'people', search],
    enabled: canSearchPeople && search.trim().length >= 2,
    queryFn: async () => unwrap(await api.GET('/api/admin/people', { params: { query: { Search: search.trim(), PageSize: 8 } } })),
  });
  const save = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/giving/gifts', {
          body: {
            fundId: form.fundId,
            amount: Number(form.amount),
            method: form.method,
            personId: person?.id ?? null,
            giverName: person ? null : form.giverName,
            note: form.note || null,
            givenOn: form.givenOn,
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['giving'] });
      onDone();
    },
  });

  return (
    <Card title="Record a gift" actions={<Button variant="ghost" onClick={onDone}>Cancel</Button>}>
      <form
        className="form-grid"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        <Field label="Fund">
          <Select required value={form.fundId} onChange={(e) => setForm({ ...form, fundId: e.target.value })}>
            <option value="">Choose…</option>
            {funds.data
              ?.filter((f) => !f.isArchived)
              .map((f) => (
                <option key={f.id} value={f.id}>
                  {f.name}
                </option>
              ))}
          </Select>
        </Field>
        <Field label="Amount (R)">
          <TextInput required type="number" min="5" step="0.01" inputMode="decimal" value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} />
        </Field>
        <Field label="Method">
          <Select value={form.method} onChange={(e) => setForm({ ...form, method: e.target.value as Method })}>
            <option value="Eft">EFT (bank transfer)</option>
            <option value="Cash">Cash</option>
          </Select>
        </Field>
        <Field label="Date received">
          <TextInput required type="date" max={today} value={form.givenOn} onChange={(e) => setForm({ ...form, givenOn: e.target.value })} />
        </Field>
        {person ? (
          <Field label="Giver">
            <div className="row">
              <strong>{person.name}</strong>
              <Button type="button" variant="ghost" onClick={() => setPerson(null)}>
                Change
              </Button>
            </div>
          </Field>
        ) : (
          <>
            {canSearchPeople && (
              <Field label="Find the giver" hint="Someone with a church record, so it shows on their statement.">
                <TextInput type="search" placeholder="Name, email or phone" value={search} onChange={(e) => setSearch(e.target.value)} />
              </Field>
            )}
            <Field label="Or the name on the statement" hint="Or “Anonymous”.">
              <TextInput value={form.giverName} onChange={(e) => setForm({ ...form, giverName: e.target.value })} maxLength={120} />
            </Field>
          </>
        )}
        <Field label="Note (optional)">
          <TextInput value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} maxLength={300} placeholder="e.g. statement reference" />
        </Field>
        {!person && matches.data && matches.data.items.length > 0 && (
          <ul className="list form-actions">
            {matches.data.items.map((p) => (
              <li key={p.id} className="list-row">
                <span>
                  {p.displayName} <span className="small muted">{p.campusName}</span>
                </span>
                <Button type="button" variant="ghost" onClick={() => setPerson({ id: p.id, name: p.displayName })}>
                  Choose
                </Button>
              </li>
            ))}
          </ul>
        )}
        <div className="form-actions">
          <ErrorNote error={save.error} />
          <Button type="submit" variant="primary" busy={save.isPending}>
            Record gift
          </Button>
        </div>
      </form>
    </Card>
  );
}

/** A giver's year: every gift and the totals, to print or send for a Section 18A certificate. */
export function GivingStatementPage() {
  const { personId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const [thisYear] = useState(() => new Date().getFullYear());
  const year = Number(params.get('year')) || thisYear;
  const statement = useQuery({
    queryKey: ['giving', 'statement', personId, year],
    queryFn: async () => unwrap(await api.GET('/api/admin/giving/statements/{personId}', { params: { path: { personId }, query: { year } } })),
  });

  return (
    <>
      <PageHeader
        title={statement.data ? `${statement.data.personName}: giving in ${year}` : 'Giving statement'}
        subtitle="Gifts received in the calendar year. Print it, or use it for a Section 18A certificate."
        actions={
          <div className="row">
            <Button variant="ghost" aria-label="Previous year" onClick={() => setParams({ year: String(year - 1) })}>
              ‹ {year - 1}
            </Button>
            <Button variant="ghost" aria-label="Next year" disabled={year >= thisYear} onClick={() => setParams({ year: String(year + 1) })}>
              {year + 1} ›
            </Button>
            <Button onClick={() => window.print()}>Print</Button>
          </div>
        }
      />
      {statement.isPending && <Loading />}
      <ErrorNote error={statement.error} />
      {statement.data && (
        <Card>
          {statement.data.gifts.length === 0 ? (
            <Empty>No gifts received in {year}.</Empty>
          ) : (
            <>
              <table className="table">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Fund</th>
                    <th>Method</th>
                    <th className="giving-amount">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  {statement.data.gifts.map((g) => (
                    <tr key={g.id}>
                      <td>{formatDate(g.givenOn)}</td>
                      <td>{g.fundName}</td>
                      <td>{methodLabel[g.method]}</td>
                      <td className="giving-amount">{rands(g.amountCents)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <ul className="list">
                {statement.data.byFund.map((f) => (
                  <li key={f.label} className="list-row">
                    <span>{f.label}</span>
                    <span>{rands(f.amountCents)}</span>
                  </li>
                ))}
                <li className="list-row">
                  <strong>Total for {year}</strong>
                  <strong>{rands(statement.data.totalCents)}</strong>
                </li>
              </ul>
            </>
          )}
        </Card>
      )}
    </>
  );
}

/** What gifts can be for. A fund no longer in use stays on old gifts but can't be chosen. */
export function FundsPage() {
  const queryClient = useQueryClient();
  const funds = useFunds();
  const done = () => void queryClient.invalidateQueries({ queryKey: ['giving'] });
  const [form, setForm] = useState({ name: '', description: '' });
  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/giving/funds', { body: { name: form.name, description: form.description || null, order: funds.data?.length ?? 0 } })),
    onSuccess: () => {
      setForm({ name: '', description: '' });
      done();
    },
  });
  const toggle = useMutation({
    mutationFn: async (f: Schemas['FundDto']) =>
      f.isArchived
        ? unwrap(await api.POST('/api/admin/giving/funds/{id}/restore', { params: { path: { id: f.id } } }))
        : unwrap(await api.POST('/api/admin/giving/funds/{id}/archive', { params: { path: { id: f.id } } })),
    onSuccess: done,
  });

  return (
    <>
      <PageHeader title="Funds" subtitle="What people can give to. They choose one when they give online." />
      {funds.isPending && <Loading />}
      <ErrorNote error={funds.error ?? toggle.error} />
      {funds.data && (
        <Card>
          <ul className="list">
            {funds.data.map((f) => (
              <li key={f.id} className={`list-row${f.isArchived ? ' kids-collected' : ''}`}>
                <span>
                  <strong>{f.name}</strong>
                  {f.description && <span className="small muted"> · {f.description}</span>}
                  {f.isArchived && <Badge>Not in use</Badge>}
                </span>
                <Button
                  variant="ghost"
                  busy={toggle.isPending && toggle.variables?.id === f.id}
                  onClick={() => (f.isArchived || window.confirm(`Stop taking gifts for ${f.name}? Past gifts keep their fund.`)) && toggle.mutate(f)}
                >
                  {f.isArchived ? 'Use again' : 'Stop using'}
                </Button>
              </li>
            ))}
          </ul>
        </Card>
      )}
      <Card title="Add a fund">
        <form
          className="form-grid"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <Field label="Name">
            <TextInput required maxLength={60} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="e.g. Building fund" />
          </Field>
          <Field label="Description (optional)">
            <TextInput maxLength={200} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
          </Field>
          <div className="form-actions">
            <ErrorNote error={create.error} />
            <Button type="submit" variant="primary" busy={create.isPending}>
              Add fund
            </Button>
          </div>
        </form>
      </Card>
    </>
  );
}
