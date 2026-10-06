import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { hhmm, positionsOf, shortDay, useSongs, useTeams, type PlanItem } from '../lib/services';
import { uploadFile } from '../lib/upload';
import { OrderEditor } from './ServicesPages';

type Team = Schemas['TeamDto'];
type Category = Schemas['CategoryDto'];

// ---------- Teams ----------

function useCategories() {
  return useQuery({ queryKey: ['services', 'categories'], queryFn: async () => unwrap(await api.GET('/api/admin/services/categories')) });
}

/** Teams grouped by category (Ministries, Disciplines, ...), their positions, and who serves in which. */
export function ServingTeamsPage() {
  const { data: access } = useAccess();
  const teams = useTeams();
  const categories = useCategories();
  const queryClient = useQueryClient();
  const [form, setForm] = useState({ name: '', categoryId: '', openToMinors: false });
  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/services/teams', {
          body: { name: form.name, description: null, openToMinors: form.openToMinors, scope: null, categoryId: form.categoryId || null },
        }),
      ),
    onSuccess: () => {
      setForm({ name: '', categoryId: form.categoryId, openToMinors: false });
      void queryClient.invalidateQueries({ queryKey: ['services', 'teams'] });
    },
  });

  const groups: { category: Category | null; teams: Team[] }[] = [
    ...(categories.data ?? []).map((c) => ({ category: c, teams: (teams.data ?? []).filter((t) => t.categoryId === c.id) })),
    { category: null, teams: (teams.data ?? []).filter((t) => !t.categoryId || !categories.data?.some((c) => c.id === t.categoryId)) },
  ].filter((g) => g.category || g.teams.length > 0);

  return (
    <>
      <PageHeader title="Teams" subtitle="Ministries, disciplines and departments, the roles in each, and who holds them. People are scheduled only to positions they hold." />
      <div className="grid-2">
        <Card title="New team">
          <form
            className="stack"
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate();
            }}
          >
            <div className="form-grid">
              <Field label="Name">
                <TextInput required maxLength={80} placeholder="e.g. Youth ministry, Worship, Finance" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
              </Field>
              <Field label="Category">
                <Select value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })}>
                  <option value="">No category</option>
                  {categories.data?.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </Select>
              </Field>
            </div>
            <label className="checkbox">
              <input type="checkbox" checked={form.openToMinors} onChange={(e) => setForm({ ...form, openToMinors: e.target.checked })} />
              Under-18s may serve on this team (always with an adult from the team)
            </label>
            <ErrorNote error={create.error} />
            <div>
              <Button variant="primary" type="submit" busy={create.isPending}>
                Add team
              </Button>
            </div>
          </form>
        </Card>
        {can(access, Permissions.servicesCategories) && <CategoriesCard categories={categories.data ?? []} />}
      </div>
      <ErrorNote error={teams.error ?? categories.error} />
      {teams.isPending ? (
        <Loading />
      ) : teams.data?.length === 0 ? (
        <Card>
          <Empty>No teams yet.</Empty>
        </Card>
      ) : (
        groups.map((g) => (
          <section key={g.category?.id ?? 'none'} className="stack">
            <div className="category-head">
              <h2>{g.category?.name ?? 'Other teams'}</h2>
              {g.category?.description && <span className="small muted">{g.category.description}</span>}
            </div>
            {g.teams.length === 0 ? (
              <p className="small muted">No teams in this category yet.</p>
            ) : (
              g.teams.map((t) => <TeamEditor key={t.id} team={t} categories={categories.data ?? []} />)
            )}
          </section>
        ))
      )}
    </>
  );
}

/** Only people allowed to manage categories see this. */
function CategoriesCard({ categories }: { categories: Category[] }) {
  const queryClient = useQueryClient();
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['services'] });
  const [name, setName] = useState('');
  const [editing, setEditing] = useState<{ id: string; name: string } | null>(null);
  const add = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/services/categories', { body: { name, description: null, order: categories.length + 1 } })),
    onSuccess: () => {
      setName('');
      refresh();
    },
  });
  const rename = useMutation({
    mutationFn: async (c: Category) =>
      unwrap(await api.PUT('/api/admin/services/categories/{id}', { params: { path: { id: c.id } }, body: { name: editing!.name, description: c.description, order: c.order } })),
    onSuccess: () => {
      setEditing(null);
      refresh();
    },
  });
  const remove = useMutation({
    mutationFn: async (id: string) => unwrap(await api.DELETE('/api/admin/services/categories/{id}', { params: { path: { id } } })),
    onSuccess: refresh,
  });

  return (
    <Card title="Categories">
      <ul className="list">
        {categories.map((c) => (
          <li key={c.id} className="list-row">
            {editing?.id === c.id ? (
              <span className="row">
                <TextInput value={editing.name} maxLength={60} onChange={(e) => setEditing({ id: c.id, name: e.target.value })} aria-label="Category name" />
                <button type="button" className="link-button" onClick={() => rename.mutate(c)}>
                  Save
                </button>
              </span>
            ) : (
              <span>{c.name}</span>
            )}
            <span className="row">
              <button type="button" className="link-button" onClick={() => setEditing({ id: c.id, name: c.name })}>
                Rename
              </button>
              <button type="button" className="link-button" onClick={() => window.confirm(`Remove ${c.name}? Its teams stay, without a category.`) && remove.mutate(c.id)}>
                Remove
              </button>
            </span>
          </li>
        ))}
      </ul>
      <form
        className="row"
        onSubmit={(e) => {
          e.preventDefault();
          add.mutate();
        }}
      >
        <TextInput required maxLength={60} placeholder="New category, e.g. Outreach" value={name} onChange={(e) => setName(e.target.value)} aria-label="New category" />
        <Button type="submit" busy={add.isPending}>
          Add
        </Button>
      </form>
      <ErrorNote error={add.error ?? rename.error ?? remove.error} />
    </Card>
  );
}

function TeamEditor({ team, categories }: { team: Team; categories: Category[] }) {
  const queryClient = useQueryClient();
  const saved = () => void queryClient.invalidateQueries({ queryKey: ['services', 'teams'] });
  const [position, setPosition] = useState('');
  const addPosition = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/services/teams/{id}/positions', { params: { path: { id: team.id } }, body: { name: position, order: team.positions.length + 1 } })),
    onSuccess: () => {
      setPosition('');
      saved();
    },
  });
  const archivePosition = useMutation({
    mutationFn: async (positionId: string) => unwrap(await api.DELETE('/api/admin/services/teams/{id}/positions/{positionId}', { params: { path: { id: team.id, positionId } } })),
    onSuccess: saved,
  });
  const saveMember = useMutation({
    mutationFn: async (m: { personId: string; positionIds: string[]; isLeader: boolean }) =>
      unwrap(await api.PUT('/api/admin/services/teams/{id}/members', { params: { path: { id: team.id } }, body: m })),
    onSuccess: saved,
  });
  const removeMember = useMutation({
    mutationFn: async (personId: string) => unwrap(await api.DELETE('/api/admin/services/teams/{id}/members/{personId}', { params: { path: { id: team.id, personId } } })),
    onSuccess: saved,
  });
  const move = useMutation({
    mutationFn: async (categoryId: string) =>
      unwrap(
        await api.PUT('/api/admin/services/teams/{id}', {
          params: { path: { id: team.id } },
          body: { name: team.name, description: team.description, openToMinors: team.openToMinors, scope: null, categoryId: categoryId || null },
        }),
      ),
    onSuccess: saved,
  });
  const toggle = (ids: string[], id: string) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]);
  const positionName = (id: string) => team.positions.find((p) => p.id === id)?.name;

  return (
    <Card
      title={
        <>
          {team.name} {team.openToMinors && <Badge>Open to under-18s</Badge>}
        </>
      }
      actions={
        <Select value={team.categoryId ?? ''} onChange={(e) => move.mutate(e.target.value)} aria-label={`Category of ${team.name}`}>
          <option value="">No category</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
      }
    >
      <div className="grid-2">
        <div className="stack-tight">
          <h3 className="small">People ({team.members.length})</h3>
          {team.members.length === 0 && <p className="small muted">No one yet.</p>}
          <ul className="list">
            {team.members.map((m) => (
              <li key={m.personId} className="review-item stack-tight">
                <span className="list-row">
                  <span>
                    <Link to={`/people/${m.personId}`}>{m.name}</Link>
                    {m.positionIds.length > 0 && <span className="small muted"> · {m.positionIds.map(positionName).filter(Boolean).join(', ')}</span>}{' '}
                    {m.isLeader && <Badge tone="accent">Leads</Badge>}
                  </span>
                  <button type="button" className="link-button" onClick={() => window.confirm(`Remove ${m.name} from ${team.name}?`) && removeMember.mutate(m.personId)}>
                    Remove
                  </button>
                </span>
                <span className="row">
                  {team.positions.map((p) => (
                    <label key={p.id} className="checkbox small">
                      <input
                        type="checkbox"
                        checked={m.positionIds.includes(p.id)}
                        onChange={() => saveMember.mutate({ personId: m.personId, positionIds: toggle(m.positionIds, p.id), isLeader: m.isLeader })}
                      />
                      {p.name}
                    </label>
                  ))}
                  <label className="checkbox small">
                    <input type="checkbox" checked={m.isLeader} onChange={() => saveMember.mutate({ personId: m.personId, positionIds: m.positionIds, isLeader: !m.isLeader })} />
                    Leads the team
                  </label>
                </span>
              </li>
            ))}
          </ul>
          <AddPerson team={team} onAdded={saved} />
        </div>
        <div className="stack-tight">
          <h3 className="small">Positions</h3>
          <ul className="list">
            {team.positions.map((p) => (
              <li key={p.id} className="list-row">
                {p.name}
                <button type="button" className="link-button" onClick={() => window.confirm(`Remove ${p.name}?`) && archivePosition.mutate(p.id)}>
                  Remove
                </button>
              </li>
            ))}
          </ul>
          <form
            className="row"
            onSubmit={(e) => {
              e.preventDefault();
              addPosition.mutate();
            }}
          >
            <TextInput required maxLength={60} placeholder="e.g. Youth pastor, Keys, Treasurer" value={position} onChange={(e) => setPosition(e.target.value)} aria-label="New position" />
            <Button type="submit" busy={addPosition.isPending}>
              Add
            </Button>
          </form>
        </div>
      </div>
      <ErrorNote error={addPosition.error ?? archivePosition.error ?? saveMember.error ?? removeMember.error ?? move.error} />
    </Card>
  );
}

const NEW_POSITION = '__new__';

/**
 * Adds someone to the team in one go: find them (or create their church record), choose their position (or type a
 * new one, e.g. "Youth pastor"), and say whether they lead the team.
 */
function AddPerson({ team, onAdded }: { team: Team; onAdded: () => void }) {
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState<'find' | 'new'>('find');
  const [search, setSearch] = useState('');
  const [personId, setPersonId] = useState<string | null>(null);
  const [newPerson, setNewPerson] = useState({ firstName: '', lastName: '', mobile: '', email: '' });
  const [positionId, setPositionId] = useState(team.positions[0]?.id ?? NEW_POSITION);
  const [positionName, setPositionName] = useState('');
  const [isLeader, setIsLeader] = useState(false);
  const people = useQuery({
    queryKey: ['people-pick', search],
    queryFn: async () => unwrap(await api.GET('/api/admin/people', { params: { query: { Search: search, Page: 1, PageSize: 6 } } })),
    enabled: mode === 'find' && search.trim().length >= 2,
  });
  const add = useMutation({
    mutationFn: async () => {
      let person = personId;
      if (mode === 'new') {
        const created = unwrap(
          await api.POST('/api/admin/people', {
            body: {
              firstName: newPerson.firstName,
              lastName: newPerson.lastName,
              preferredName: null,
              dateOfBirth: null,
              gender: null,
              campusId: null,
              membershipStatusId: null,
              email: newPerson.email || null,
              mobile: newPerson.mobile || null,
            },
          }),
        );
        person = created.id;
      }

      let position = positionId;
      if (positionId === NEW_POSITION) {
        const updated = unwrap(
          await api.POST('/api/admin/services/teams/{id}/positions', { params: { path: { id: team.id } }, body: { name: positionName, order: team.positions.length + 1 } }),
        );
        position = updated.positions.find((p) => p.name.toLowerCase() === positionName.trim().toLowerCase())?.id ?? '';
      }

      const existing = team.members.find((m) => m.personId === person);
      return unwrap(
        await api.PUT('/api/admin/services/teams/{id}/members', {
          params: { path: { id: team.id } },
          body: { personId: person!, positionIds: [...new Set([...(existing?.positionIds ?? []), position].filter(Boolean))], isLeader: isLeader || !!existing?.isLeader },
        }),
      );
    },
    onSuccess: () => {
      setOpen(false);
      setSearch('');
      setPersonId(null);
      setNewPerson({ firstName: '', lastName: '', mobile: '', email: '' });
      setPositionName('');
      setIsLeader(false);
      onAdded();
    },
  });

  if (!open) {
    return (
      <div>
        <Button onClick={() => setOpen(true)}>Add someone</Button>
      </div>
    );
  }

  const chosen = people.data?.items.find((p) => p.id === personId);
  const ready =
    (mode === 'find' ? !!personId : !!newPerson.firstName.trim() && !!newPerson.lastName.trim()) &&
    (positionId !== NEW_POSITION || !!positionName.trim());

  return (
    <form
      className="ai-suggestion stack"
      onSubmit={(e) => {
        e.preventDefault();
        add.mutate();
      }}
    >
      <div className="row">
        <Button variant={mode === 'find' ? 'primary' : 'ghost'} onClick={() => setMode('find')}>
          Someone on our records
        </Button>
        <Button variant={mode === 'new' ? 'primary' : 'ghost'} onClick={() => setMode('new')}>
          Someone new
        </Button>
      </div>

      {mode === 'find' ? (
        <div className="stack-tight">
          <TextInput
            autoFocus
            placeholder="Name, email or phone"
            value={chosen ? chosen.displayName : search}
            onChange={(e) => {
              setPersonId(null);
              setSearch(e.target.value);
            }}
            aria-label="Find a person"
          />
          {!personId && people.data && (
            <ul className="list">
              {people.data.items.map((p) => (
                <li key={p.id} className="list-row">
                  <span>
                    {p.displayName} <span className="small muted">{p.primaryMobile ?? p.primaryEmail ?? ''}</span>
                  </span>
                  <button type="button" className="link-button" onClick={() => setPersonId(p.id)}>
                    Choose
                  </button>
                </li>
              ))}
              {people.data.items.length === 0 && (
                <li className="small muted">
                  No one found.{' '}
                  <button type="button" className="link-button" onClick={() => setMode('new')}>
                    Add them as someone new
                  </button>
                </li>
              )}
            </ul>
          )}
        </div>
      ) : (
        <div className="form-grid">
          <Field label="First name">
            <TextInput required maxLength={60} value={newPerson.firstName} onChange={(e) => setNewPerson({ ...newPerson, firstName: e.target.value })} />
          </Field>
          <Field label="Last name">
            <TextInput required maxLength={60} value={newPerson.lastName} onChange={(e) => setNewPerson({ ...newPerson, lastName: e.target.value })} />
          </Field>
          <Field label="Mobile (optional)">
            <TextInput type="tel" value={newPerson.mobile} onChange={(e) => setNewPerson({ ...newPerson, mobile: e.target.value })} />
          </Field>
          <Field label="Email (optional)">
            <TextInput type="email" value={newPerson.email} onChange={(e) => setNewPerson({ ...newPerson, email: e.target.value })} />
          </Field>
        </div>
      )}

      <div className="form-grid">
        <Field label="Position">
          <Select value={positionId} onChange={(e) => setPositionId(e.target.value)}>
            {team.positions.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
            <option value={NEW_POSITION}>New position…</option>
          </Select>
        </Field>
        {positionId === NEW_POSITION && (
          <Field label="Name of the position">
            <TextInput required maxLength={60} placeholder="e.g. Youth pastor" value={positionName} onChange={(e) => setPositionName(e.target.value)} />
          </Field>
        )}
      </div>
      <label className="checkbox">
        <input type="checkbox" checked={isLeader} onChange={(e) => setIsLeader(e.target.checked)} />
        Leads {team.name}
      </label>
      <ErrorNote error={add.error} />
      <div className="row">
        <Button variant="primary" type="submit" busy={add.isPending} disabled={!ready}>
          Add to {team.name}
        </Button>
        <Button variant="ghost" onClick={() => setOpen(false)}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

// ---------- Songs ----------

/** The song library, and the CCLI usage report. */
export function SongsPage() {
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  const songs = useSongs(q);
  const [title, setTitle] = useState('');
  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/services/songs', { body: { title, author: null, ccliNumber: null, themes: [], lyrics: null, referenceUrl: null, arrangements: [] } })),
    onSuccess: (s) => navigate(`/services/songs/${s.id}`),
  });

  return (
    <>
      <PageHeader title="Songs" subtitle="Keys, chord charts, recordings and lyrics (the church holds a CCLI licence)." />
      <div className="grid-2">
        <Card title="Add a song">
          <form
            className="row"
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate();
            }}
          >
            <TextInput required maxLength={150} placeholder="Title" value={title} onChange={(e) => setTitle(e.target.value)} aria-label="Song title" />
            <Button variant="primary" type="submit" busy={create.isPending}>
              Add
            </Button>
          </form>
          <ErrorNote error={create.error} />
        </Card>
        <CcliReport />
      </div>
      <Card>
        <TextInput placeholder="Search by title, author or CCLI number" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search songs" />
        <ErrorNote error={songs.error} />
        {songs.isPending ? (
          <Loading />
        ) : songs.data?.length === 0 ? (
          <Empty>No songs yet.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Song</th>
                <th>Keys</th>
                <th>CCLI</th>
                <th>Last used</th>
                <th>Times</th>
              </tr>
            </thead>
            <tbody>
              {songs.data?.map((s) => (
                <tr key={s.id}>
                  <td>
                    <Link to={`/services/songs/${s.id}`}>{s.title}</Link>
                    {s.author && <span className="small muted"> · {s.author}</span>}
                  </td>
                  <td>{s.keys.join(', ') || '—'}</td>
                  <td className="small">{s.ccliNumber ?? '—'}</td>
                  <td className="small">{s.lastUsed ? shortDay(s.lastUsed) : 'Never'}</td>
                  <td>{s.timesUsed}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}

function CcliReport() {
  // This calendar year so far.
  const [range, setRange] = useState(() => {
    const now = new Date();
    return { from: `${now.getFullYear()}-01-01`, to: now.toISOString().slice(0, 10) };
  });
  const report = useQuery({
    queryKey: ['services', 'ccli', range],
    queryFn: async () => unwrap(await api.GET('/api/admin/services/songs/report', { params: { query: range } })),
  });
  const csv = () => {
    const rows = [['Title', 'Author', 'CCLI number', 'Times used'], ...(report.data ?? []).map((r) => [r.title, r.author ?? '', r.ccliNumber ?? '', String(r.times)])];
    const blob = new Blob([rows.map((r) => r.map((c) => `"${c.replace(/"/g, '""')}"`).join(',')).join('\n')], { type: 'text/csv' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `ccli-report-${range.from}-to-${range.to}.csv`;
    a.click();
  };
  return (
    <Card title="CCLI report">
      <div className="form-grid">
        <Field label="From">
          <TextInput type="date" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} />
        </Field>
        <Field label="To">
          <TextInput type="date" value={range.to} onChange={(e) => setRange({ ...range, to: e.target.value })} />
        </Field>
      </div>
      <p className="small muted">
        {report.data?.length ?? 0} songs used, {report.data?.reduce((s, r) => s + r.times, 0) ?? 0} times in all.{' '}
        {report.data?.some((r) => !r.ccliNumber) && <span className="danger-text">Some songs have no CCLI number.</span>}
      </p>
      <Button onClick={csv} disabled={!report.data?.length}>
        Download CSV
      </Button>
      <ErrorNote error={report.error} />
    </Card>
  );
}

type ArrangementForm = Schemas['Arrangement'];

/** One song: details, lyrics, and its arrangements with charts and recordings. */
export function SongPage() {
  const { id = '' } = useParams();
  const song = useQuery({ queryKey: ['services', 'song', id], queryFn: async () => unwrap(await api.GET('/api/admin/services/songs/{id}', { params: { path: { id } } })) });
  if (song.isPending) return <Loading />;
  if (!song.data) return <ErrorNote error={song.error} />;
  return <SongEditor key={song.data.id + song.data.arrangements.length} song={song.data} />;
}

function SongEditor({ song }: { song: Schemas['SongDto'] }) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState({
    title: song.title,
    author: song.author ?? '',
    ccliNumber: song.ccliNumber ?? '',
    themes: song.themes.join(', '),
    lyrics: song.lyrics ?? '',
    referenceUrl: song.referenceUrl ?? '',
  });
  const [arrangements, setArrangements] = useState<ArrangementForm[]>(song.arrangements);
  const [uploading, setUploading] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.PUT('/api/admin/services/songs/{id}', {
          params: { path: { id: song.id } },
          body: {
            title: form.title,
            author: form.author || null,
            ccliNumber: form.ccliNumber || null,
            themes: form.themes.split(',').map((t) => t.trim()).filter(Boolean),
            lyrics: form.lyrics || null,
            referenceUrl: form.referenceUrl || null,
            arrangements,
          },
        }),
      ),
    onSuccess: (s) => {
      queryClient.setQueryData(['services', 'song', s.id], s);
      void queryClient.invalidateQueries({ queryKey: ['services', 'songs'] });
    },
  });
  const update = (i: number, patch: Partial<ArrangementForm>) => setArrangements(arrangements.map((a, j) => (j === i ? { ...a, ...patch } : a)));
  const upload = async (i: number, file: File | undefined, kind: 'chart' | 'audio') => {
    if (!file) return;
    setUploading(`${i}-${kind}`);
    try {
      const mediaKind = kind === 'audio' ? 'Audio' : file.type === 'application/pdf' ? 'NotesPdf' : 'Image';
      const asset = await uploadFile(mediaKind, file, () => undefined);
      update(i, kind === 'audio' ? { audioUrl: asset.url } : { chartUrl: asset.url, chartFileName: file.name });
    } finally {
      setUploading(null);
    }
  };

  return (
    <>
      <PageHeader title={song.title} subtitle={<Link to="/services/songs">All songs</Link>} actions={<span className="small muted">Used {song.timesUsed} times{song.lastUsed ? `, last ${shortDay(song.lastUsed)}` : ''}</span>} />
      <form
        className="grid-2"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        <Card title="Song">
          <div className="stack">
            <Field label="Title">
              <TextInput required maxLength={150} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
            </Field>
            <div className="form-grid">
              <Field label="Author">
                <TextInput maxLength={200} value={form.author} onChange={(e) => setForm({ ...form, author: e.target.value })} />
              </Field>
              <Field label="CCLI song number">
                <TextInput inputMode="numeric" pattern="[0-9]*" maxLength={12} value={form.ccliNumber} onChange={(e) => setForm({ ...form, ccliNumber: e.target.value })} />
              </Field>
            </div>
            <Field label="Themes" hint="Comma separated, e.g. Faith, Grace">
              <TextInput value={form.themes} onChange={(e) => setForm({ ...form, themes: e.target.value })} />
            </Field>
            <Field label="Recording to learn it from (optional)">
              <TextInput type="url" placeholder="https://www.youtube.com/watch?v=…" value={form.referenceUrl} onChange={(e) => setForm({ ...form, referenceUrl: e.target.value })} />
            </Field>
            <Field label="Lyrics" hint="Shown on the music stand and in the app for the band.">
              <textarea className="input" rows={12} value={form.lyrics} onChange={(e) => setForm({ ...form, lyrics: e.target.value })} />
            </Field>
          </div>
        </Card>
        <Card title="Arrangements">
          <div className="stack">
            {arrangements.map((a, i) => (
              <div key={a.id + i} className="review-item stack-tight">
                <div className="form-grid">
                  <Field label="Name">
                    <TextInput required maxLength={60} value={a.name} onChange={(e) => update(i, { name: e.target.value })} />
                  </Field>
                  <Field label="Key">
                    <TextInput maxLength={10} value={a.key ?? ''} onChange={(e) => update(i, { key: e.target.value || null })} />
                  </Field>
                  <Field label="BPM">
                    <TextInput type="number" min={1} max={399} value={a.bpm ?? ''} onChange={(e) => update(i, { bpm: e.target.value ? Number(e.target.value) : null })} />
                  </Field>
                </div>
                <div className="row">
                  {a.chartUrl ? (
                    <a href={a.chartUrl} target="_blank" rel="noreferrer">
                      {a.chartFileName ?? 'Chart'}
                    </a>
                  ) : (
                    <span className="small muted">No chart</span>
                  )}
                  <label className="btn btn-secondary">
                    {uploading === `${i}-chart` ? 'Uploading…' : a.chartUrl ? 'Replace chart' : 'Upload chart (PDF or image)'}
                    <input type="file" hidden accept="application/pdf,image/jpeg,image/png,image/webp" onChange={(e) => void upload(i, e.target.files?.[0], 'chart')} />
                  </label>
                </div>
                <div className="row">
                  {a.audioUrl ? <audio controls preload="none" src={a.audioUrl} /> : <span className="small muted">No recording</span>}
                  <label className="btn btn-secondary">
                    {uploading === `${i}-audio` ? 'Uploading…' : a.audioUrl ? 'Replace recording' : 'Upload recording (MP3)'}
                    <input type="file" hidden accept="audio/mpeg,audio/mp4,audio/x-m4a,.mp3,.m4a" onChange={(e) => void upload(i, e.target.files?.[0], 'audio')} />
                  </label>
                </div>
                <TextInput placeholder="Notes for the band (optional)" maxLength={1000} value={a.notes ?? ''} onChange={(e) => update(i, { notes: e.target.value || null })} aria-label="Notes" />
                {arrangements.length > 1 && (
                  <button type="button" className="link-button" onClick={() => setArrangements(arrangements.filter((_, j) => j !== i))}>
                    Remove arrangement
                  </button>
                )}
              </div>
            ))}
            <Button
              onClick={() =>
                setArrangements([
                  ...arrangements,
                  { id: '00000000-0000-0000-0000-000000000000', name: 'Acoustic', key: null, bpm: null, chartUrl: null, chartFileName: null, audioUrl: null, notes: null },
                ])
              }
            >
              Add an arrangement
            </Button>
            <ErrorNote error={save.error} />
            <Button variant="primary" type="submit" busy={save.isPending}>
              Save song
            </Button>
          </div>
        </Card>
      </form>
    </>
  );
}

// ---------- Templates ----------

/** Service types: the usual order of service and team needs that new plans start from. */
export function ServiceTypesPage() {
  const queryClient = useQueryClient();
  const types = useQuery({ queryKey: ['services', 'types'], queryFn: async () => unwrap(await api.GET('/api/admin/services/types')) });
  const [editing, setEditing] = useState<Schemas['ServiceTypeDto'] | 'new' | null>(null);
  return (
    <>
      <PageHeader
        title="Templates"
        subtitle="Your usual services. A new plan starts with the template's order of service and the positions it needs."
        actions={
          !editing && (
            <Button variant="primary" onClick={() => setEditing('new')}>
              New template
            </Button>
          )
        }
      />
      {editing && (
        <TemplateEditor
          existing={editing === 'new' ? null : editing}
          onDone={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: ['services', 'types'] });
          }}
        />
      )}
      <Card>
        <ErrorNote error={types.error} />
        {types.data?.length === 0 ? (
          <Empty>No templates yet. Most churches start with one for each Sunday service.</Empty>
        ) : (
          <ul className="list">
            {types.data?.map((t) => (
              <li key={t.id} className="list-row">
                <span>
                  <strong>{t.name}</strong> <span className="small muted">starts {hhmm(t.startTime)} · {t.items.length} items · {t.needs.reduce((s, n) => s + n.count, 0)} people</span>
                </span>
                <button type="button" className="link-button" onClick={() => setEditing(t)}>
                  Edit
                </button>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </>
  );
}

function TemplateEditor({ existing, onDone }: { existing: Schemas['ServiceTypeDto'] | null; onDone: () => void }) {
  const teams = useTeams();
  const [name, setName] = useState(existing?.name ?? 'Sunday 09:00');
  const [startTime, setStartTime] = useState(hhmm(existing?.startTime) || '09:00');
  const [items, setItems] = useState<PlanItem[]>(existing?.items ?? []);
  const [needs, setNeeds] = useState<Record<string, number>>(Object.fromEntries((existing?.needs ?? []).map((n) => [n.positionId, n.count])));
  const save = useMutation({
    mutationFn: async () => {
      const body = { name, startTime: `${startTime}:00`, items, needs: Object.entries(needs).filter(([, c]) => c > 0).map(([positionId, count]) => ({ positionId, count })), scope: null };
      return existing
        ? unwrap(await api.PUT('/api/admin/services/types/{id}', { params: { path: { id: existing.id } }, body }))
        : unwrap(await api.POST('/api/admin/services/types', { body }));
    },
    onSuccess: onDone,
  });

  return (
    <Card title={existing ? `Edit ${existing.name}` : 'New template'}>
      <div className="stack">
        <div className="form-grid">
          <Field label="Name">
            <TextInput required maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label="Starts at">
            <TextInput type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} />
          </Field>
        </div>
        <OrderEditor items={items} onChange={setItems} startTime={startTime} />
        <h3 className="small">People needed</h3>
        <div className="form-grid">
          {positionsOf(teams.data).map((p) => (
            <Field key={p.id} label={p.label}>
              <Select value={needs[p.id] ?? 0} onChange={(e) => setNeeds({ ...needs, [p.id]: Number(e.target.value) })}>
                {[0, 1, 2, 3, 4, 5, 6].map((n) => (
                  <option key={n} value={n}>
                    {n}
                  </option>
                ))}
              </Select>
            </Field>
          ))}
        </div>
        <ErrorNote error={save.error} />
        <div className="row">
          <Button variant="primary" busy={save.isPending} onClick={() => save.mutate()}>
            Save template
          </Button>
          <Button onClick={onDone}>Cancel</Button>
        </div>
      </div>
    </Card>
  );
}
