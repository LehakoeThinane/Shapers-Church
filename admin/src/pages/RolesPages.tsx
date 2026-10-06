import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDate, unwrap, type Schemas } from '../lib/api';
import { can, covers, Permissions, scopesFor, useAccess } from '../lib/access';
import { useScopes } from '../lib/queries';
import { scopeLabel } from '../lib/scope-context';

type Role = Schemas['RoleDto'];
type Permission = Schemas['PermissionDto'];
type Scope = NonNullable<ReturnType<typeof useScopes>['data']>[number];

const ACCESS_MANAGER = 'Access manager';

/** Plain names for the parts of the system a permission belongs to. */
const areas: Record<string, string> = {
  people: 'People',
  groups: 'Home cells',
  prayer: 'Prayer',
  events: 'Events',
  services: 'Services and serving',
  media: 'Sermons, livestream and chat',
  communications: 'Announcements and messages',
  content: 'Website and app',
  church: 'Church setup',
  identity: 'Roles and access',
  privacy: 'Privacy (POPIA)',
  platform: 'Audit log',
  assist: 'AI help',
};

const usePermissions = () => useQuery({ queryKey: ['permissions'], queryFn: async () => unwrap(await api.GET('/api/admin/permissions')) });
const useRoles = () => useQuery({ queryKey: ['roles'], queryFn: async () => unwrap(await api.GET('/api/admin/roles')) });

function byArea(permissions: Permission[]) {
  const groups = new Map<string, Permission[]>();
  for (const p of permissions) groups.set(p.module, [...(groups.get(p.module) ?? []), p]);
  return [...groups.entries()]
    .map(([module, items]) => ({ module, label: areas[module] ?? module, items }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

const peopleCount = (n: number) => (n === 0 ? 'Nobody yet' : n === 1 ? '1 person' : `${n} people`);

// ---------- All roles ----------

export function RolesPage() {
  const { data: access } = useAccess();
  const roles = useRoles();
  const accessManager = roles.data?.find((r) => r.isSystem && r.name === ACCESS_MANAGER);

  return (
    <>
      <PageHeader
        title="Roles and access"
        subtitle="A role is what someone can do. Open one to see what it allows and who has it."
        actions={
          can(access, Permissions.rolesManage) && (
            <Link to="/access/roles/new" className="btn btn-primary">
              New role
            </Link>
          )
        }
      />
      <ErrorNote error={roles.error} />
      {roles.isPending && <Loading />}
      <div className="role-grid">
        {roles.data?.map((r) => (
          <Link key={r.id} to={`/access/roles/${r.id}`} className="role-tile glass">
            <span className="stack-tight">
              <strong>{r.name}</strong>
              <span className="small muted">{peopleCount(r.people)}</span>
            </span>
            {!r.isSystem ? <Badge tone="accent">Custom</Badge> : r.isCustomised ? <Badge>Changed</Badge> : null}
          </Link>
        ))}
      </div>
      {accessManager && (
        <p className="small muted">
          Church administrators and access managers can change roles and who has them. To let someone else do this, open{' '}
          <Link to={`/access/roles/${accessManager.id}`}>Access manager</Link> and add them.
        </p>
      )}
    </>
  );
}

// ---------- One role ----------

export function RolePage() {
  const { id } = useParams();
  const isNew = id === undefined;
  const roles = useRoles();
  const permissions = usePermissions();
  const role = roles.data?.find((r) => r.id === id);

  if (roles.isPending || permissions.isPending) return <Loading />;
  if (!isNew && !role) {
    return (
      <>
        <Link to="/access" className="small">
          ← All roles
        </Link>
        <Empty>That role doesn&apos;t exist any more.</Empty>
      </>
    );
  }

  return (
    <>
      <Link to="/access" className="small">
        ← All roles
      </Link>
      <ErrorNote error={roles.error ?? permissions.error} />
      {/* Keyed so the editor starts from the saved role whenever it changes. */}
      <RoleEditor key={role ? `${role.id}:${role.permissions.join(',')}:${role.description ?? ''}` : 'new'} role={role} permissions={permissions.data ?? []} />
      {role && <RoleHolders role={role} />}
    </>
  );
}

function RoleEditor({ role, permissions }: { role: Role | undefined; permissions: Permission[] }) {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [editing, setEditing] = useState(!role);
  const [name, setName] = useState(role?.name ?? '');
  const [description, setDescription] = useState(role?.description ?? '');
  const [selected, setSelected] = useState<string[]>(role?.permissions ?? []);
  const canManage = can(access, Permissions.rolesManage) && (role?.canEdit ?? true);
  const done = (saved: Role) => {
    void queryClient.invalidateQueries({ queryKey: ['roles'] });
    void queryClient.invalidateQueries({ queryKey: ['me', 'access'] });
    setEditing(false);
    if (!role) navigate(`/access/roles/${saved.id}`, { replace: true });
  };

  const save = useMutation({
    mutationFn: async () => {
      const body = { name, description: description.trim() || null, permissions: selected };
      return role
        ? unwrap(await api.PUT('/api/admin/roles/{id}', { params: { path: { id: role.id } }, body }))
        : unwrap(await api.POST('/api/admin/roles', { body }));
    },
    onSuccess: done,
  });
  const reset = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/roles/{id}/reset', { params: { path: { id: role!.id } } })),
    onSuccess: done,
  });

  const allowed = permissions.filter((p) => (editing ? true : selected.includes(p.key)));
  const groups = byArea(allowed);
  const toggle = (key: string, on: boolean) => setSelected(on ? [...selected, key] : selected.filter((k) => k !== key));

  return (
    <>
      <PageHeader
        title={role?.name ?? 'New role'}
        subtitle={role?.description ?? (role ? undefined : 'Choose a name and what people with this role can do.')}
        actions={
          canManage &&
          role &&
          !editing && (
            <Button variant="primary" onClick={() => setEditing(true)}>
              Change what it allows
            </Button>
          )
        }
      />
      {role && !role.canEdit && <p className="note small">This role always has full access, so the church can never lock itself out. It can&apos;t be changed.</p>}
      {role?.isCustomised && <p className="note small">The church has changed this built-in role.</p>}

      <Card title={editing ? 'What people with this role can do' : `What they can do (${selected.length})`}>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          {editing && (
            <div className="form-grid">
              {(!role || !role.isSystem) && (
                <Field label="Role name">
                  <TextInput required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
                </Field>
              )}
              <Field label="Short description (optional)">
                <TextInput maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} />
              </Field>
            </div>
          )}

          {groups.length === 0 && <p className="muted small">Nothing yet.</p>}
          <div className="permission-areas">
            {groups.map((g) => (
              <fieldset key={g.module} className="permission-area">
                <legend className="small muted">{g.label}</legend>
                {g.items.map((p) =>
                  editing ? (
                    <label key={p.key} className="checkbox">
                      <input type="checkbox" checked={selected.includes(p.key)} onChange={(e) => toggle(p.key, e.target.checked)} />
                      <span>
                        {p.description} {p.isSensitive && <Badge tone="accent">Personal details</Badge>}
                      </span>
                    </label>
                  ) : (
                    <p key={p.key} className="small permission-line">
                      {p.description}
                    </p>
                  ),
                )}
              </fieldset>
            ))}
          </div>

          {editing && (
            <>
              <p className="small muted">You can only add things you&apos;re allowed to do yourself. Changes apply to everyone with this role straight away.</p>
              <ErrorNote error={save.error ?? reset.error} />
              <div className="row">
                <Button variant="primary" type="submit" busy={save.isPending} disabled={selected.length === 0 || !name.trim()}>
                  {role ? 'Save changes' : 'Create role'}
                </Button>
                <Button
                  variant="ghost"
                  onClick={() => {
                    if (!role) {
                      navigate('/access');
                      return;
                    }
                    setSelected(role.permissions);
                    setDescription(role.description ?? '');
                    setEditing(false);
                  }}
                >
                  Cancel
                </Button>
                {role?.isCustomised && (
                  <Button variant="ghost" busy={reset.isPending} onClick={() => reset.mutate()}>
                    Put back the defaults
                  </Button>
                )}
              </div>
            </>
          )}
        </form>
      </Card>
    </>
  );
}

/** Who has the role, and where; people with access rights can add and remove them here. */
function RoleHolders({ role }: { role: Role }) {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const { data: scopes = [] } = useScopes();
  const holders = useQuery({
    queryKey: ['roles', role.id, 'people'],
    queryFn: async () => unwrap(await api.GET('/api/admin/roles/{id}/people', { params: { path: { id: role.id } } })),
  });
  const manageScopes = scopesFor(access, Permissions.grantsManage);
  const grantable = scopes.filter((s) => manageScopes.some((m) => covers(m, s.path)));
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['roles'] });
    void queryClient.invalidateQueries({ queryKey: ['person-access'] });
  };
  const revoke = useMutation({
    mutationFn: async (grantId: string) => unwrap(await api.DELETE('/api/admin/grants/{id}', { params: { path: { id: grantId } } })),
    onSuccess: refresh,
  });

  return (
    <Card title={`People with this role (${holders.data?.length ?? role.people})`}>
      <ErrorNote error={holders.error ?? revoke.error} />
      {holders.isPending && <Loading />}
      {holders.data?.length === 0 && <p className="muted small">Nobody has this role yet.</p>}
      {holders.data && holders.data.length > 0 && (
        <ul className="list">
          {holders.data.map((h) => (
            <li key={h.grantId} className="list-row">
              <span>
                <Link to={`/people/${h.personId}`}>{h.displayName}</Link>{' '}
                <span className="small muted">
                  at {h.scopeName}
                  {h.expiresAt ? `, until ${formatDate(h.expiresAt)}` : ''}
                </span>
              </span>
              {manageScopes.some((m) => covers(m, h.scope)) && (
                <Button variant="ghost" onClick={() => revoke.mutate(h.grantId)} disabled={revoke.isPending}>
                  Remove
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {grantable.length > 0 && <GiveRole role={role} scopes={grantable} onGiven={refresh} />}
    </Card>
  );
}

function GiveRole({ role, scopes, onGiven }: { role: Role; scopes: Scope[]; onGiven: () => void }) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [person, setPerson] = useState<{ id: string; name: string } | null>(null);
  const [scope, setScope] = useState(scopes[0]?.path ?? '');
  const people = useQuery({
    queryKey: ['people-pick', search],
    queryFn: async () => unwrap(await api.GET('/api/admin/people', { params: { query: { Search: search, Page: 1, PageSize: 6 } } })),
    enabled: open && !person && search.trim().length >= 2,
  });
  const give = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/grants', { body: { personId: person!.id, roleId: role.id, scope, expiresAt: null, reason: null } })),
    onSuccess: () => {
      setOpen(false);
      setSearch('');
      setPerson(null);
      onGiven();
    },
  });

  if (!open) {
    return (
      <div>
        <Button onClick={() => setOpen(true)}>Give this role to someone</Button>
      </div>
    );
  }

  return (
    <form
      className="ai-suggestion stack"
      onSubmit={(e) => {
        e.preventDefault();
        give.mutate();
      }}
    >
      <Field label="Who">
        <TextInput
          autoFocus
          placeholder="Name, email or phone"
          value={person ? person.name : search}
          onChange={(e) => {
            setPerson(null);
            setSearch(e.target.value);
          }}
        />
      </Field>
      {!person && people.data && (
        <ul className="list">
          {people.data.items.map((p) => (
            <li key={p.id} className="list-row">
              <span>
                {p.displayName} <span className="small muted">{p.primaryEmail ?? p.primaryMobile ?? ''}</span>
              </span>
              <button type="button" className="link-button" onClick={() => setPerson({ id: p.id, name: p.displayName })}>
                Choose
              </button>
            </li>
          ))}
          {people.data.items.length === 0 && <li className="small muted">No one found. Add them under People first.</li>}
        </ul>
      )}
      {scopes.length > 1 && (
        <Field label="Where">
          <Select required value={scope} onChange={(e) => setScope(e.target.value)}>
            {scopes.map((s) => (
              <option key={s.path} value={s.path}>
                {scopeLabel(s)}
              </option>
            ))}
          </Select>
        </Field>
      )}
      <p className="small muted">They need a login first. If they don&apos;t have one, create it on their person record.</p>
      <ErrorNote error={give.error} />
      {give.error && person && (
        <Link to={`/people/${person.id}`} className="small">
          Open {person.name}&apos;s record
        </Link>
      )}
      <div className="row">
        <Button variant="primary" type="submit" busy={give.isPending} disabled={!person || !scope}>
          Give {role.name}
        </Button>
        <Button variant="ghost" onClick={() => setOpen(false)}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
