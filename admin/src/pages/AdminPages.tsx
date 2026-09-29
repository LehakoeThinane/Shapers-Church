import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import QRCode from 'qrcode';
import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { useCampuses } from '../lib/queries';

// ---------- Duplicates ----------

export function DuplicatesPage() {
  const queryClient = useQueryClient();
  const duplicates = useQuery({ queryKey: ['duplicates'], queryFn: async () => unwrap(await api.GET('/api/admin/duplicates')) });
  const done = () => {
    void queryClient.invalidateQueries({ queryKey: ['duplicates'] });
    void queryClient.invalidateQueries({ queryKey: ['people'] });
  };
  const merge = useMutation({
    mutationFn: async (body: Schemas['MergeRequest']) => unwrap(await api.POST('/api/admin/people/merge', { body })),
    onSuccess: done,
  });
  const dismiss = useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/admin/duplicates/{id}/dismiss', { params: { path: { id } } })),
    onSuccess: done,
  });

  return (
    <>
      <PageHeader
        title="Possible duplicates"
        subtitle="Visitor cards, event sign-ups and giving create repeat records. Check each pair before merging: a merge can't be undone automatically."
      />
      <ErrorNote error={duplicates.error ?? merge.error ?? dismiss.error} />
      {duplicates.isPending ? (
        <Loading />
      ) : duplicates.data?.length === 0 ? (
        <Card>
          <Empty>No possible duplicates right now.</Empty>
        </Card>
      ) : (
        duplicates.data?.map((d) => (
          <Card key={d.id} title={`${d.personA.displayName} and ${d.personB.displayName}`} actions={<Badge tone="accent">{d.reasons}</Badge>}>
            <div className="grid-2">
              {[d.personA, d.personB].map((p) => (
                <div key={p.id} className="stack">
                  <Link to={`/people/${p.id}`}>{p.displayName}</Link>
                  <span className="small muted">
                    {p.campusName ?? 'Church-wide'} · {p.membershipStatus}
                  </span>
                  <span className="small">{p.primaryMobile ?? 'no mobile'} · {p.primaryEmail ?? 'no email'}</span>
                </div>
              ))}
            </div>
            <div className="row">
              <Button busy={merge.isPending} onClick={() => merge.mutate({ survivorId: d.personA.id, duplicateId: d.personB.id })}>
                Keep {d.personA.firstName} (left)
              </Button>
              <Button busy={merge.isPending} onClick={() => merge.mutate({ survivorId: d.personB.id, duplicateId: d.personA.id })}>
                Keep {d.personB.firstName} (right)
              </Button>
              <Button variant="ghost" busy={dismiss.isPending} onClick={() => dismiss.mutate(d.id)}>
                Not the same person
              </Button>
            </div>
          </Card>
        ))
      )}
    </>
  );
}

// ---------- Campuses and ministries ----------

export function ChurchPage() {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const campuses = useCampuses();
  const ministries = useQuery({ queryKey: ['ministries'], queryFn: async () => unwrap(await api.GET('/api/admin/ministries')) });
  const [campus, setCampus] = useState({ name: '', slug: '' });
  const [ministry, setMinistry] = useState({ name: '', slug: '', campusId: '' });

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['campuses'] });
    void queryClient.invalidateQueries({ queryKey: ['ministries'] });
    void queryClient.invalidateQueries({ queryKey: ['scopes'] });
  };
  const addCampus = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/campuses', { body: { name: campus.name, slug: campus.slug || campus.name, address: null } })),
    onSuccess: () => {
      setCampus({ name: '', slug: '' });
      refresh();
    },
  });
  const addMinistry = useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/admin/ministries', { body: { name: ministry.name, slug: ministry.slug || ministry.name, campusId: ministry.campusId || null } })),
    onSuccess: () => {
      setMinistry({ name: '', slug: '', campusId: '' });
      refresh();
    },
  });
  const campusName = (id: string | null) => (id ? campuses.data?.find((c) => c.id === id)?.name ?? '—' : 'Church-wide');

  return (
    <>
      <PageHeader title="Campuses and ministries" subtitle="These form the scopes that access is granted on. Short names are fixed once created." />
      <div className="grid-2">
        <Card title="Campuses">
          <ErrorNote error={campuses.error} />
          <ul className="list">
            {campuses.data?.map((c) => (
              <li key={c.id} className="list-row">
                <span>
                  {c.name} {c.isPrimary && <Badge>Primary</Badge>}
                </span>
                <span className="muted small">{c.address ? `${c.address.line1}, ${c.address.suburb ?? c.address.city}` : 'No address yet'}</span>
              </li>
            ))}
          </ul>
          {can(access, Permissions.campusesManage) && (
            <form
              className="row"
              onSubmit={(e) => {
                e.preventDefault();
                addCampus.mutate();
              }}>
              <TextInput required placeholder="Campus name" value={campus.name} onChange={(e) => setCampus({ ...campus, name: e.target.value })} />
              <TextInput placeholder="Short name (optional)" value={campus.slug} onChange={(e) => setCampus({ ...campus, slug: e.target.value })} />
              <Button type="submit" busy={addCampus.isPending}>
                Add campus
              </Button>
            </form>
          )}
          <ErrorNote error={addCampus.error} />
        </Card>

        <Card title="Ministries">
          <ErrorNote error={ministries.error} />
          {ministries.data?.length === 0 && <Empty>No ministries yet.</Empty>}
          <ul className="list">
            {ministries.data?.map((m) => (
              <li key={m.id} className="list-row">
                <span>{m.name}</span>
                <span className="muted small">{campusName(m.campusId)}</span>
              </li>
            ))}
          </ul>
          {can(access, Permissions.ministriesManage) && (
            <form
              className="row"
              onSubmit={(e) => {
                e.preventDefault();
                addMinistry.mutate();
              }}>
              <TextInput required placeholder="Ministry name" value={ministry.name} onChange={(e) => setMinistry({ ...ministry, name: e.target.value })} />
              <Select value={ministry.campusId} onChange={(e) => setMinistry({ ...ministry, campusId: e.target.value })}>
                <option value="">Church-wide</option>
                {campuses.data?.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
              <Button type="submit" busy={addMinistry.isPending}>
                Add ministry
              </Button>
            </form>
          )}
          <ErrorNote error={addMinistry.error} />
        </Card>
      </div>
    </>
  );
}

// ---------- Roles ----------

export function RolesPage() {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: ['roles'], queryFn: async () => unwrap(await api.GET('/api/admin/roles')) });
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: async () => unwrap(await api.GET('/api/admin/permissions')) });
  const [name, setName] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const describe = (key: string) => permissions.data?.find((p) => p.key === key)?.description ?? key;

  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/roles', { body: { name, description: null, permissions: selected } })),
    onSuccess: () => {
      setName('');
      setSelected([]);
      void queryClient.invalidateQueries({ queryKey: ['roles'] });
    },
  });

  return (
    <>
      <PageHeader title="Roles and access" subtitle="A role is a set of permissions. Give someone a role at a campus or ministry from their person record." />
      <ErrorNote error={roles.error ?? permissions.error} />
      <div className="grid-2">
        {roles.data?.map((r) => (
          <Card key={r.id} title={r.name} actions={r.isSystem ? <Badge>Built in</Badge> : <Badge tone="accent">Custom</Badge>}>
            {r.description && <p className="muted small">{r.description}</p>}
            <ul className="list small">
              {r.permissions.map((p) => (
                <li key={p}>{describe(p)}</li>
              ))}
            </ul>
          </Card>
        ))}
      </div>

      {can(access, Permissions.rolesManage) && (
        <Card title="New custom role">
          <form
            className="stack"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              create.mutate();
            }}>
            <Field label="Role name">
              <TextInput required value={name} onChange={(e) => setName(e.target.value)} />
            </Field>
            <div className="checks">
              {permissions.data?.map((p) => (
                <label key={p.key} className="checkbox">
                  <input
                    type="checkbox"
                    checked={selected.includes(p.key)}
                    onChange={(e) => setSelected(e.target.checked ? [...selected, p.key] : selected.filter((k) => k !== p.key))}
                  />
                  {p.description} {p.isSensitive && <Badge tone="accent">Sensitive</Badge>}
                </label>
              ))}
            </div>
            <ErrorNote error={create.error} />
            <Button variant="primary" type="submit" busy={create.isPending} disabled={selected.length === 0}>
              Create role
            </Button>
          </form>
        </Card>
      )}
    </>
  );
}

// ---------- Audit log ----------

export function AuditPage() {
  const [entityType, setEntityType] = useState('');
  const [entityId, setEntityId] = useState('');
  const [page, setPage] = useState(1);
  const audit = useQuery({
    queryKey: ['audit', entityType, entityId, page],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/admin/audit', {
          params: { query: { entityType: entityType || undefined, entityId: entityId || undefined, page, pageSize: 50 } },
        }),
      ),
  });

  return (
    <>
      <PageHeader title="Audit log" subtitle="Every change, and every view of a person's full record. Entries can't be edited or deleted." />
      <Card>
        <div className="filters">
          <Field label="Record type">
            <Select value={entityType} onChange={(e) => setEntityType(e.target.value)}>
              <option value="">Any</option>
              <option value="person">Person</option>
              <option value="household">Household</option>
              <option value="grant">Access grant</option>
              <option value="role">Role</option>
              <option value="user">Login</option>
            </Select>
          </Field>
          <Field label="Record ID">
            <TextInput value={entityId} onChange={(e) => setEntityId(e.target.value.trim())} />
          </Field>
        </div>
        <ErrorNote error={audit.error} />
        {audit.isPending ? (
          <Loading />
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>When</th>
                <th>Action</th>
                <th>Record</th>
                <th>By</th>
              </tr>
            </thead>
            <tbody>
              {audit.data?.items.map((e) => (
                <tr key={e.id}>
                  <td className="nowrap">{formatDateTime(e.occurredAt)}</td>
                  <td>
                    {e.action} {e.isSensitiveRead && <Badge tone="accent">Sensitive view</Badge>}
                  </td>
                  <td>{e.entityType === 'person' && e.entityId ? <Link to={`/people/${e.entityId}`}>{e.entityType}</Link> : e.entityType}</td>
                  <td>{e.actorPersonId ? <Link to={`/people/${e.actorPersonId}`}>staff member</Link> : 'system'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <div className="pager">
          <Button disabled={page <= 1} onClick={() => setPage(page - 1)}>
            Newer
          </Button>
          <Button disabled={!audit.data || page * 50 >= audit.data.total} onClick={() => setPage(page + 1)}>
            Older
          </Button>
        </div>
      </Card>
    </>
  );
}

// ---------- Two-step verification ----------

export function SecurityPage() {
  const queryClient = useQueryClient();
  const status = useQuery({ queryKey: ['2fa'], queryFn: async () => unwrap(await api.GET('/api/auth/2fa')) });
  const [setup, setSetup] = useState<Schemas['AuthenticatorSetup'] | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);

  useEffect(() => {
    if (!setup) return;
    QRCode.toDataURL(setup.authenticatorUri, { margin: 1, width: 220 }).then(setQr, () => setQr(null));
  }, [setup]);

  const begin = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/auth/2fa/setup')),
    onSuccess: setSetup,
  });
  const enable = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/auth/2fa/enable', { body: { code } })),
    onSuccess: (result) => {
      setRecoveryCodes(result.codes);
      setSetup(null);
      void queryClient.invalidateQueries();
    },
  });

  return (
    <>
      <PageHeader title="Security" subtitle="Two-step verification protects the personal information of everyone in the church database." />
      <Card title="Two-step verification">
        <ErrorNote error={status.error ?? begin.error ?? enable.error} />
        {status.data?.enabled && !recoveryCodes && (
          <p>
            <Badge tone="success">On</Badge> {status.data.recoveryCodesLeft} recovery codes left.
          </p>
        )}

        {recoveryCodes && (
          <div className="note note-accent">
            <p>Two-step verification is on. Save these recovery codes somewhere safe. Each works once if you lose your phone.</p>
            <pre className="codes">{recoveryCodes.join('\n')}</pre>
          </div>
        )}

        {!status.data?.enabled && !setup && !recoveryCodes && (
          <Button variant="primary" busy={begin.isPending} onClick={() => begin.mutate()}>
            Set up an authenticator app
          </Button>
        )}

        {setup && (
          <form
            className="stack"
            onSubmit={(e) => {
              e.preventDefault();
              enable.mutate();
            }}>
            <p>Scan this with Google Authenticator, Microsoft Authenticator or 1Password, then enter the 6-digit code it shows.</p>
            {qr && <img className="qr" src={qr} alt="QR code for your authenticator app" width={220} height={220} />}
            <p className="small muted">
              Can't scan? Enter this key: <code>{setup.sharedKey}</code>
            </p>
            <Field label="6-digit code">
              <TextInput inputMode="numeric" autoComplete="one-time-code" required value={code} onChange={(e) => setCode(e.target.value)} />
            </Field>
            <Button variant="primary" type="submit" busy={enable.isPending}>
              Turn on two-step verification
            </Button>
          </form>
        )}
      </Card>
    </>
  );
}
