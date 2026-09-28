import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDate, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, covers, Permissions, scopesFor, useAccess } from '../lib/access';
import { useCampuses, useMembershipStatuses, useScopes } from '../lib/queries';
import { scopeLabel } from '../lib/scope-context';

type Person = Schemas['PersonDetailDto'];

const consentLabels: Record<string, string> = {
  'processing.church_record': 'Keep a church record',
  'communications.email': 'Email',
  'communications.sms': 'SMS',
  'communications.whatsapp': 'WhatsApp',
  'communications.push': 'App notifications',
  'media.photos': 'Photos and video',
};

export function PersonPage() {
  const { id = '' } = useParams();
  const { data: access } = useAccess();
  const person = useQuery({
    queryKey: ['person', id],
    queryFn: async () => unwrap(await api.GET('/api/admin/people/{id}', { params: { path: { id } } })),
  });

  if (person.isPending) return <Loading />;
  if (person.error || !person.data) return <ErrorNote error={person.error ?? new Error('Not found')} />;

  const p = person.data;
  const editable = p.status !== 'Merged' && scopesFor(access, Permissions.peopleEdit).some((s) => covers(s, p.scope));

  return (
    <>
      <PageHeader
        title={p.displayName}
        subtitle={
          <>
            {p.campusName ?? 'Church-wide'} · {p.membershipStatus.name} · added {formatDate(p.createdAt)}
          </>
        }
        actions={
          <>
            {p.status !== 'Active' && <Badge tone={p.status === 'Merged' ? 'danger' : 'neutral'}>{p.status}</Badge>}
            {p.hasLogin && <Badge tone="success">Has login</Badge>}
          </>
        }
      />

      {p.mergedIntoId && (
        <p className="note note-accent">
          This record was merged. <Link to={`/people/${p.mergedIntoId}`}>Open the surviving record</Link>.
        </p>
      )}

      <div className="grid-2">
        <DetailsCard key={p.updatedAt} person={p} editable={editable} />
        <ContactsCard person={p} editable={editable} />
        <StatusCard person={p} editable={editable} />
        <HouseholdsCard person={p} editable={editable} />
        <ConsentCard person={p} />
        {can(access, Permissions.usersView) && <AccessCard person={p} />}
      </div>
    </>
  );
}

function useSavePerson(id: string) {
  const queryClient = useQueryClient();
  return (updated: Person) => {
    queryClient.setQueryData(['person', id], updated);
    void queryClient.invalidateQueries({ queryKey: ['people'] });
  };
}

function DetailsCard({ person, editable }: { person: Person; editable: boolean }) {
  const save = useSavePerson(person.id);
  const [form, setForm] = useState({
    firstName: person.firstName,
    lastName: person.lastName,
    preferredName: person.preferredName ?? '',
    dateOfBirth: person.dateOfBirth ?? '',
    gender: person.gender ?? '',
  });

  const mutation = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT('/api/admin/people/{id}', {
          params: { path: { id: person.id } },
          body: {
            firstName: form.firstName,
            lastName: form.lastName,
            preferredName: form.preferredName || null,
            dateOfBirth: form.dateOfBirth || null,
            gender: (form.gender || null) as Schemas['UpdatePersonRequest']['gender'],
          },
        }),
      ),
    onSuccess: save,
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    mutation.mutate();
  };

  return (
    <Card title="Details">
      <form className="form-grid" onSubmit={submit}>
        <Field label="First name">
          <TextInput required disabled={!editable} value={form.firstName} onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
        </Field>
        <Field label="Last name">
          <TextInput required disabled={!editable} value={form.lastName} onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
        </Field>
        <Field label="Preferred name">
          <TextInput disabled={!editable} value={form.preferredName} onChange={(e) => setForm({ ...form, preferredName: e.target.value })} />
        </Field>
        <Field label="Date of birth">
          <TextInput type="date" disabled={!editable} value={form.dateOfBirth} onChange={(e) => setForm({ ...form, dateOfBirth: e.target.value })} />
        </Field>
        <Field label="Gender">
          <Select disabled={!editable} value={form.gender} onChange={(e) => setForm({ ...form, gender: e.target.value })}>
            <option value="">Not recorded</option>
            <option value="Female">Female</option>
            <option value="Male">Male</option>
          </Select>
        </Field>
        {editable && (
          <div className="form-actions">
            <ErrorNote error={mutation.error} />
            <Button variant="primary" type="submit" busy={mutation.isPending}>
              Save details
            </Button>
          </div>
        )}
      </form>
    </Card>
  );
}

function ContactsCard({ person, editable }: { person: Person; editable: boolean }) {
  const save = useSavePerson(person.id);
  const [type, setType] = useState<Schemas['AddContactRequest']['type']>('Mobile');
  const [value, setValue] = useState('');

  const add = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/admin/people/{id}/contacts', { params: { path: { id: person.id } }, body: { type, value, isPrimary: false } })),
    onSuccess: (updated) => {
      save(updated);
      setValue('');
    },
  });
  const remove = useMutation({
    mutationFn: async (contactId: string) =>
      unwrap(await api.DELETE('/api/admin/people/{id}/contacts/{contactId}', { params: { path: { id: person.id, contactId } } })),
    onSuccess: save,
  });

  return (
    <Card title="Contact details">
      {person.contacts.length === 0 ? (
        <Empty>No contact details yet.</Empty>
      ) : (
        <ul className="list">
          {person.contacts.map((c) => (
            <li key={c.id} className="list-row">
              <span>
                <span className="muted small">{c.type}</span> {c.value}
                {c.isPrimary && <Badge>Primary</Badge>}
                {c.isVerified && <Badge tone="success">Verified</Badge>}
              </span>
              {editable && (
                <Button variant="ghost" onClick={() => remove.mutate(c.id)} disabled={remove.isPending}>
                  Remove
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {editable && (
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault();
            add.mutate();
          }}>
          <Select value={type} onChange={(e) => setType(e.target.value as typeof type)}>
            <option value="Mobile">Mobile</option>
            <option value="WhatsApp">WhatsApp</option>
            <option value="Email">Email</option>
          </Select>
          <TextInput required placeholder={type === 'Email' ? 'name@example.com' : '082 123 4567'} value={value} onChange={(e) => setValue(e.target.value)} />
          <Button type="submit" busy={add.isPending}>
            Add
          </Button>
        </form>
      )}
      <ErrorNote error={add.error ?? remove.error} />
    </Card>
  );
}

function StatusCard({ person, editable }: { person: Person; editable: boolean }) {
  const save = useSavePerson(person.id);
  const { data: statuses } = useMembershipStatuses();
  const { data: campuses } = useCampuses();
  const [statusId, setStatusId] = useState(person.membershipStatus.id);
  const [campusId, setCampusId] = useState('');

  const change = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/admin/people/{id}/status', { params: { path: { id: person.id } }, body: { membershipStatusId: statusId, effectiveDate: null } })),
    onSuccess: save,
  });
  const move = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/people/{id}/campus', { params: { path: { id: person.id } }, body: { campusId } })),
    onSuccess: save,
  });

  return (
    <Card title="Church journey">
      {editable && (
        <div className="stack">
          <div className="row">
            <Select value={statusId} onChange={(e) => setStatusId(e.target.value)}>
              {statuses?.filter((s) => s.isActive).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
            <Button onClick={() => change.mutate()} busy={change.isPending} disabled={statusId === person.membershipStatus.id}>
              Change status
            </Button>
          </div>
          {campuses && campuses.length > 1 && (
            <div className="row">
              <Select value={campusId} onChange={(e) => setCampusId(e.target.value)}>
                <option value="">Move to campus…</option>
                {campuses.filter((c) => c.scope !== person.scope).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
              <Button onClick={() => move.mutate()} busy={move.isPending} disabled={!campusId}>
                Move
              </Button>
            </div>
          )}
          <ErrorNote error={change.error ?? move.error} />
        </div>
      )}
      <ul className="list">
        {person.statusHistory.map((h, i) => (
          <li key={i} className="list-row">
            <span>{h.from ? `${h.from} → ${h.to}` : h.to}</span>
            <span className="muted small">{formatDate(h.effectiveDate)}</span>
          </li>
        ))}
      </ul>
    </Card>
  );
}

function HouseholdsCard({ person, editable }: { person: Person; editable: boolean }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(`${person.lastName} household`);
  const create = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/admin/households', { body: { name, campusId: null, members: [{ personId: person.id, role: 'Adult' }] } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['person', person.id] }),
  });

  return (
    <Card title="Households">
      {person.households.length === 0 ? (
        <Empty>Not in a household yet.</Empty>
      ) : (
        person.households.map((h) => (
          <div key={h.id} className="stack">
            <strong>{h.name}</strong>
            <ul className="list">
              {h.members.map((m) => (
                <li key={m.personId} className="list-row">
                  <Link to={`/people/${m.personId}`}>{m.displayName}</Link>
                  <span className="muted small">
                    {m.role}
                    {m.isPrimaryContact ? ' · primary contact' : ''}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        ))
      )}
      {editable && (
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}>
          <TextInput value={name} onChange={(e) => setName(e.target.value)} />
          <Button type="submit" busy={create.isPending}>
            New household
          </Button>
        </form>
      )}
      <ErrorNote error={create.error} />
    </Card>
  );
}

function ConsentCard({ person }: { person: Person }) {
  return (
    <Card title="Consent">
      {person.consents.length === 0 ? (
        <Empty>No consent recorded. Ask before sending messages or keeping more than basic details.</Empty>
      ) : (
        <ul className="list">
          {person.consents.map((c) => (
            <li key={c.purpose} className="list-row">
              <span>{consentLabels[c.purpose] ?? c.purpose}</span>
              <span>
                <Badge tone={c.granted ? 'success' : 'danger'}>{c.granted ? 'Yes' : 'No'}</Badge>{' '}
                <span className="muted small">
                  {c.source} · {formatDate(c.recordedAt)}
                </span>
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function AccessCard({ person }: { person: Person }) {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const { data: scopes = [] } = useScopes();
  const personAccess = useQuery({
    queryKey: ['person-access', person.id],
    queryFn: async () => unwrap(await api.GET('/api/admin/people/{personId}/access', { params: { path: { personId: person.id } } })),
  });
  const roles = useQuery({ queryKey: ['roles'], queryFn: async () => unwrap(await api.GET('/api/admin/roles')) });

  const manageScopes = scopesFor(access, Permissions.grantsManage);
  const grantableScopes = scopes.filter((s) => manageScopes.some((m) => covers(m, s.path)));
  const [email, setEmail] = useState('');
  const [setupLink, setSetupLink] = useState<string | null>(null);
  const [roleId, setRoleId] = useState('');
  const [scope, setScope] = useState('');
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['person-access', person.id] });

  const prepare = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/admin/people/{personId}/staff-login', { params: { path: { personId: person.id } }, body: { email } })),
    onSuccess: (setup) => {
      const url = new URL('/set-password', window.location.origin);
      url.searchParams.set('userId', setup.userId);
      url.searchParams.set('token', setup.setupToken);
      setSetupLink(url.toString());
      void refresh();
    },
  });
  const grant = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/admin/grants', { body: { personId: person.id, roleId, scope, expiresAt: null, reason: null } })),
    onSuccess: () => {
      setRoleId('');
      void refresh();
    },
  });
  const revoke = useMutation({
    mutationFn: async (grantId: string) => unwrap(await api.DELETE('/api/admin/grants/{id}', { params: { path: { id: grantId } } })),
    onSuccess: refresh,
  });

  if (personAccess.isPending) return <Card title="Login and access"><Loading /></Card>;
  const a = personAccess.data;
  const activeGrants = a?.grants.filter((g) => g.isActive) ?? [];

  return (
    <Card title="Login and access">
      <ErrorNote error={personAccess.error} />
      {a?.userId ? (
        <p className="small">
          {a.email ?? a.phone ?? 'Phone login'} · {a.twoFactorEnabled ? 'two-step on' : 'two-step off'} · last signed in {formatDateTime(a.lastSignInAt)}
        </p>
      ) : (
        <p className="muted small">No login yet.</p>
      )}

      {activeGrants.length > 0 && (
        <ul className="list">
          {activeGrants.map((g) => (
            <li key={g.id} className="list-row">
              <span>
                {g.roleName} <span className="muted small">at {scopes.find((s) => s.path === g.scope)?.name ?? g.scope}</span>
              </span>
              {manageScopes.some((m) => covers(m, g.scope)) && (
                <Button variant="ghost" onClick={() => revoke.mutate(g.id)} disabled={revoke.isPending}>
                  Remove
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}

      {manageScopes.length > 0 && (
        <div className="stack">
          {!a?.hasPassword && (
            <form
              className="row"
              onSubmit={(e) => {
                e.preventDefault();
                prepare.mutate();
              }}>
              <TextInput type="email" required placeholder="Work email for a staff login" value={email} onChange={(e) => setEmail(e.target.value)} />
              <Button type="submit" busy={prepare.isPending}>
                Create staff login
              </Button>
            </form>
          )}
          {setupLink && (
            <p className="note note-accent small">
              Send this one-time link to {email} privately. It lets them choose a password:
              <br />
              <code className="break">{setupLink}</code>
            </p>
          )}
          {a?.userId && (
            <form
              className="row"
              onSubmit={(e) => {
                e.preventDefault();
                grant.mutate();
              }}>
              <Select required value={roleId} onChange={(e) => setRoleId(e.target.value)}>
                <option value="">Give role…</option>
                {roles.data?.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </Select>
              <Select required value={scope} onChange={(e) => setScope(e.target.value)}>
                <option value="">at…</option>
                {grantableScopes.map((s) => (
                  <option key={s.path} value={s.path}>
                    {scopeLabel(s)}
                  </option>
                ))}
              </Select>
              <Button type="submit" busy={grant.isPending}>
                Grant
              </Button>
            </form>
          )}
          <ErrorNote error={prepare.error ?? grant.error ?? revoke.error} />
        </div>
      )}
    </Card>
  );
}
