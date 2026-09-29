import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { useCampuses, useMembershipStatuses } from '../lib/queries';
import { useScope } from '../lib/scope-context';

const PAGE_SIZE = 25;

function useDebounced<T>(value: T, ms = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return debounced;
}

export function PeoplePage() {
  const { data: access } = useAccess();
  const { current: scope } = useScope();
  const { data: statuses } = useMembershipStatuses();
  const [search, setSearch] = useState('');
  const [statusId, setStatusId] = useState('');
  const [includeInactive, setIncludeInactive] = useState(false);
  const debouncedSearch = useDebounced(search);

  // Any change of filter starts again at page 1.
  const filterKey = JSON.stringify([debouncedSearch, statusId, includeInactive, scope]);
  const [paging, setPaging] = useState({ filterKey, page: 1 });
  const page = paging.filterKey === filterKey ? paging.page : 1;
  const setPage = (next: number) => setPaging({ filterKey, page: next });

  const people = useQuery({
    queryKey: ['people', { search: debouncedSearch, statusId, includeInactive, scope, page }],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/admin/people', {
          params: {
            query: {
              Search: debouncedSearch || undefined,
              Scope: scope ?? undefined,
              MembershipStatusId: statusId || undefined,
              IncludeInactive: includeInactive,
              Page: page,
              PageSize: PAGE_SIZE,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
  });

  const pages = people.data ? Math.max(1, Math.ceil(people.data.total / PAGE_SIZE)) : 1;

  return (
    <>
      <PageHeader
        title="People"
        subtitle={people.data ? `${people.data.total} ${people.data.total === 1 ? 'person' : 'people'}` : undefined}
        actions={can(access, Permissions.peopleEdit) && (
          <Link className="btn btn-primary" to="/people/new">
            Add person
          </Link>
        )}
      />

      <Card>
        <div className="filters">
          <Field label="Search">
            <TextInput placeholder="Name, email or phone" value={search} onChange={(e) => setSearch(e.target.value)} />
          </Field>
          <Field label="Membership status">
            <Select value={statusId} onChange={(e) => setStatusId(e.target.value)}>
              <option value="">Any</option>
              {statuses?.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </Field>
          <label className="checkbox">
            <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
            Include inactive
          </label>
        </div>

        <ErrorNote error={people.error} />
        {people.isPending ? (
          <Loading />
        ) : people.data && people.data.items.length === 0 ? (
          <Empty>No one matches. Try a different search, or widen the scope at the top.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Campus</th>
                <th>Status</th>
                <th>Email</th>
                <th>Mobile</th>
              </tr>
            </thead>
            <tbody>
              {people.data?.items.map((p) => (
                <tr key={p.id}>
                  <td>
                    <Link to={`/people/${p.id}`}>{p.displayName}</Link>
                    {p.status !== 'Active' && <Badge>{p.status}</Badge>}
                  </td>
                  <td>{p.campusName ?? 'Church-wide'}</td>
                  <td>{p.membershipStatus}</td>
                  <td>{p.primaryEmail ?? '—'}</td>
                  <td>{p.primaryMobile ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {pages > 1 && (
          <div className="pager">
            <Button disabled={page <= 1} onClick={() => setPage(page - 1)}>
              Previous
            </Button>
            <span className="muted small">
              Page {page} of {pages}
            </span>
            <Button disabled={page >= pages} onClick={() => setPage(page + 1)}>
              Next
            </Button>
          </div>
        )}
      </Card>
    </>
  );
}

export function NewPersonPage() {
  const navigate = useNavigate();
  const { data: campuses } = useCampuses();
  const { data: statuses } = useMembershipStatuses();
  const [form, setForm] = useState<Schemas['CreatePersonRequest']>({
    firstName: '',
    lastName: '',
    preferredName: null,
    dateOfBirth: null,
    gender: null,
    campusId: null,
    membershipStatusId: null,
    email: null,
    mobile: null,
  });
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const set = (patch: Partial<Schemas['CreatePersonRequest']>) => setForm({ ...form, ...patch });

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const person = unwrap(await api.POST('/api/admin/people', { body: form }));
      navigate(`/people/${person.id}`);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <PageHeader title="Add person" subtitle="Only collect what the church needs. You can add more later." />
      <Card>
        <form className="form-grid" onSubmit={submit}>
          <Field label="First name">
            <TextInput required value={form.firstName} onChange={(e) => set({ firstName: e.target.value })} />
          </Field>
          <Field label="Last name">
            <TextInput required value={form.lastName} onChange={(e) => set({ lastName: e.target.value })} />
          </Field>
          <Field label="Preferred name" hint="What they like to be called, if different.">
            <TextInput value={form.preferredName ?? ''} onChange={(e) => set({ preferredName: e.target.value || null })} />
          </Field>
          <Field label="Date of birth">
            <TextInput type="date" value={form.dateOfBirth ?? ''} onChange={(e) => set({ dateOfBirth: e.target.value || null })} />
          </Field>
          <Field label="Mobile">
            <TextInput type="tel" placeholder="082 123 4567" value={form.mobile ?? ''} onChange={(e) => set({ mobile: e.target.value || null })} />
          </Field>
          <Field label="Email">
            <TextInput type="email" value={form.email ?? ''} onChange={(e) => set({ email: e.target.value || null })} />
          </Field>
          <Field label="Campus">
            <Select value={form.campusId ?? ''} onChange={(e) => set({ campusId: e.target.value || null })}>
              <option value="">Primary campus</option>
              {campuses?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Membership status">
            <Select value={form.membershipStatusId ?? ''} onChange={(e) => set({ membershipStatusId: e.target.value || null })}>
              <option value="">Default ({statuses?.find((s) => s.isDefault)?.name ?? 'Visitor'})</option>
              {statuses?.filter((s) => s.isActive).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </Field>
          <div className="form-actions">
            <ErrorNote error={error} />
            <Button variant="primary" type="submit" busy={busy}>
              Add person
            </Button>
          </div>
        </form>
      </Card>
    </>
  );
}
