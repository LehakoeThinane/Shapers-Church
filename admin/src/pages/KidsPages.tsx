import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, TextInput } from '../components/ui';
import { api, formatDate, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';

type Label = Schemas['KidsLabelDto'];
type ChildToday = Schemas['CheckedInChildDto'];

const time = (iso: string) => new Intl.DateTimeFormat('en-ZA', { timeZone: 'Africa/Johannesburg', hour: '2-digit', minute: '2-digit' }).format(new Date(iso));

/** Name labels for the label printer. The rest of the page is hidden while printing. */
function PrintLabels({ labels, onDone }: { labels: Label[]; onDone: () => void }) {
  const done = useRef(onDone);
  useEffect(() => {
    done.current = onDone;
  });

  // Once per set of labels, not on every render.
  useEffect(() => {
    if (labels.length === 0) return;
    const finished = () => done.current();
    window.addEventListener('afterprint', finished, { once: true });
    window.print();
    return () => window.removeEventListener('afterprint', finished);
  }, [labels]);

  return (
    <div className="kids-print" aria-hidden="true">
      {labels.map((l) => (
        <div key={l.checkInId} className="kids-label">
          <strong className="kids-label-name">{l.childName}</strong>
          <span>{l.className}</span>
          {l.hasCareNotes && <span className="kids-label-alert">⚠ Read care notes</span>}
          <span className="kids-label-code">{l.pickupCode}</span>
          <span className="kids-label-date">{formatDate(l.date)}</span>
        </div>
      ))}
    </div>
  );
}

/** Today: who is in each class, handing children back, care notes and labels. */
export function KidsTodayPage() {
  const { data: access } = useAccess();
  const queryClient = useQueryClient();
  const today = useQuery({ queryKey: ['kids', 'today'], queryFn: async () => unwrap(await api.GET('/api/admin/kids/today')), refetchInterval: 30_000 });
  const [printing, setPrinting] = useState<Label[]>([]);
  const print = useMutation({
    mutationFn: async (checkInId: string) => unwrap(await api.GET('/api/admin/kids/check-ins/{id}/label', { params: { path: { id: checkInId } } })),
    onSuccess: (label) => setPrinting([label]),
  });

  const all = today.data?.classes.flatMap((c) => c.children) ?? [];
  const here = all.filter((c) => !c.collectedAt).length;

  return (
    <>
      <PageHeader
        title="Kids"
        subtitle={today.data ? `${formatDate(today.data.date)}: ${here} ${here === 1 ? 'child' : 'children'} in class, ${all.length - here} collected.` : 'Who is in each class today.'}
      />

      <Pickup onDone={() => void queryClient.invalidateQueries({ queryKey: ['kids', 'today'] })} />

      {today.isPending && <Loading />}
      <ErrorNote error={today.error ?? print.error} />

      {today.data && (
        <div className="kids-classes">
          {today.data.classes.map((c) => (
            <Card
              key={c.classId}
              title={
                <>
                  {c.name} <span className="small muted">{c.toAge > 0 ? `${c.fromAge}–${c.toAge}` : ''}</span>
                </>
              }
              actions={<Badge tone={c.children.some((k) => !k.collectedAt) ? 'accent' : 'neutral'}>{c.children.filter((k) => !k.collectedAt).length}</Badge>}
            >
              {c.children.length === 0 && <Empty>Nobody yet.</Empty>}
              <ul className="list">
                {c.children.map((k) => (
                  <ChildRow key={k.checkInId} child={k} canReadNotes={can(access, Permissions.kidsCareView)} onPrint={() => print.mutate(k.checkInId)} />
                ))}
              </ul>
            </Card>
          ))}
          {today.data.classes.length === 0 && <Empty>No classes are set up yet. Someone with the kids set-up permission can add them under Classes.</Empty>}
        </div>
      )}

      <PrintLabels labels={printing} onDone={() => setPrinting([])} />
    </>
  );
}

function ChildRow({ child: k, canReadNotes, onPrint }: { child: ChildToday; canReadNotes: boolean; onPrint: () => void }) {
  const [open, setOpen] = useState(false);
  const notes = useQuery({
    queryKey: ['kids', 'care-notes', k.childId],
    queryFn: async () => unwrap(await api.GET('/api/admin/kids/children/{childId}/care-notes', { params: { path: { childId: k.childId } } })),
    enabled: open,
    staleTime: 0,
    gcTime: 0,
  });

  return (
    <li className={`list-row kids-child${k.collectedAt ? ' kids-collected' : ''}`}>
      <div className="stack-tight">
        <strong>
          {k.name}
          {k.age !== null && k.age !== undefined && <span className="small muted"> · {k.age}</span>}
        </strong>
        <span className="small muted">
          In at {time(k.checkedInAt)}
          {k.method === 'Desk' ? ' at the desk' : ' from the app'}
          {k.collectedAt ? ` · collected at ${time(k.collectedAt)}` : ''}
        </span>
        {open && (
          <div className="note note-accent small" role="region" aria-label={`Care notes for ${k.name}`}>
            {notes.isPending && 'Loading…'}
            <ErrorNote error={notes.error} />
            {notes.data && (
              <>
                {notes.data.allergies && (
                  <p>
                    <strong>Allergies:</strong> {notes.data.allergies}
                  </p>
                )}
                {notes.data.medical && (
                  <p>
                    <strong>Medical:</strong> {notes.data.medical}
                  </p>
                )}
                {notes.data.other && (
                  <p>
                    <strong>Also:</strong> {notes.data.other}
                  </p>
                )}
              </>
            )}
          </div>
        )}
      </div>
      <div className="row">
        {k.hasCareNotes &&
          (canReadNotes ? (
            <Button variant="ghost" aria-expanded={open} onClick={() => setOpen(!open)}>
              ⚠ {open ? 'Hide care notes' : 'Care notes'}
            </Button>
          ) : (
            <Badge tone="danger">⚠ Care notes: ask a leader</Badge>
          ))}
        {!k.collectedAt && (
          <Button variant="ghost" onClick={onPrint}>
            Print label
          </Button>
        )}
      </div>
    </li>
  );
}

/** The parent shows their code; the team checks which children it belongs to and hands them over. */
function Pickup({ onDone }: { onDone: () => void }) {
  const [code, setCode] = useState('');
  const [handedOver, setHandedOver] = useState<string | null>(null);
  const find = useMutation({
    mutationFn: async (value: string) => unwrap(await api.GET('/api/admin/kids/pickup/{code}', { params: { path: { code: value } } })),
    onSuccess: () => setHandedOver(null),
  });
  const handOver = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/kids/check-out', { body: { code: find.data!.code, checkInIds: find.data!.children.map((c) => c.checkInId) } })),
    onSuccess: (result) => {
      setHandedOver(result.children.map((c) => c.name).join(', '));
      setCode('');
      find.reset();
      onDone();
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (code.trim()) find.mutate(code.trim());
  };

  return (
    <Card title="Hand children back">
      <form className="row" onSubmit={submit}>
        <TextInput
          aria-label="Pickup code"
          placeholder="Parent's pickup code, e.g. K7MX"
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          autoComplete="off"
          maxLength={8}
          className="input kids-code-input"
        />
        <Button type="submit" variant="primary" busy={find.isPending}>
          Find
        </Button>
      </form>
      {find.error && (
        <p className="note note-danger" role="alert">
          No child is waiting with that code. Check it with the parent. If it still doesn’t match, don’t hand anyone over: ask a leader.
        </p>
      )}
      {find.data && (
        <div className="stack">
          <p className="small muted">Code {find.data.code} is for:</p>
          <ul className="list">
            {find.data.children.map((c) => (
              <li key={c.checkInId} className="list-row">
                <strong>{c.name}</strong>
                <span className="small muted">
                  {c.className} · in at {time(c.checkedInAt)}
                </span>
              </li>
            ))}
          </ul>
          <ErrorNote error={handOver.error} />
          <div className="row">
            <Button
              variant="primary"
              busy={handOver.isPending}
              onClick={() => window.confirm(`Hand over ${find.data!.children.map((c) => c.name).join(' and ')}? Only do this once you've seen the code on the parent's phone or tag.`) && handOver.mutate()}
            >
              Hand over {find.data.children.length === 1 ? 'this child' : `these ${find.data.children.length} children`}
            </Button>
            <Button variant="ghost" onClick={() => find.reset()}>
              Cancel
            </Button>
          </div>
        </div>
      )}
      {handedOver && (
        <p className="note" role="status">
          Handed over: {handedOver}.
        </p>
      )}
    </Card>
  );
}

type ChildForm = { firstName: string; lastName: string; dateOfBirth: string; allergies: string; medical: string; other: string };
const emptyChild = (lastName = ''): ChildForm => ({ firstName: '', lastName, dateOfBirth: '', allergies: '', medical: '', other: '' });

/** A visiting family without the app: the parent's details, each child, and one pickup code for them all. */
export function KidsDeskPage() {
  const queryClient = useQueryClient();
  const [parent, setParent] = useState({ firstName: '', lastName: '', mobile: '' });
  const [children, setChildren] = useState<ChildForm[]>([emptyChild()]);
  const [consent, setConsent] = useState(false);
  const [printing, setPrinting] = useState<Label[]>([]);
  const checkIn = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/kids/desk-check-in', {
          body: {
            parentFirstName: parent.firstName,
            parentLastName: parent.lastName,
            parentMobile: parent.mobile,
            consent,
            children: children.map((c) => ({
              firstName: c.firstName,
              lastName: c.lastName || parent.lastName,
              dateOfBirth: c.dateOfBirth,
              allergies: c.allergies || null,
              medical: c.medical || null,
              other: c.other || null,
            })),
          },
        }),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['kids', 'today'] }),
  });

  const update = (i: number, change: Partial<ChildForm>) => setChildren(children.map((c, j) => (j === i ? { ...c, ...change } : c)));
  const reset = () => {
    setParent({ firstName: '', lastName: '', mobile: '' });
    setChildren([emptyChild()]);
    setConsent(false);
    checkIn.reset();
  };

  if (checkIn.data) {
    const result = checkIn.data;
    return (
      <>
        <PageHeader title="Checked in" subtitle="Give the parent their pickup code: they show it to collect their children." />
        <Card>
          <p className="muted">Pickup code</p>
          <p className="kids-big-code" aria-live="polite">
            {result.pickupCode}
          </p>
          <ul className="list">
            {result.labels.map((l) => (
              <li key={l.checkInId} className="list-row">
                <strong>{l.childName}</strong>
                <span className="row">
                  {l.className}
                  {l.hasCareNotes && <Badge tone="danger">⚠ Care notes</Badge>}
                </span>
              </li>
            ))}
          </ul>
          <div className="row">
            <Button variant="primary" onClick={() => setPrinting(result.labels)}>
              Print name labels
            </Button>
            <Button onClick={reset}>Check in another family</Button>
          </div>
        </Card>
        <PrintLabels labels={printing} onDone={() => setPrinting([])} />
      </>
    );
  }

  return (
    <>
      <PageHeader title="Check in at the desk" subtitle="For visiting families without the app. Parents with the app check their children in themselves." />
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault();
          checkIn.mutate();
        }}
      >
        <Card title="Parent or guardian">
          <div className="form-grid">
            <Field label="First name">
              <TextInput required value={parent.firstName} onChange={(e) => setParent({ ...parent, firstName: e.target.value })} autoComplete="off" />
            </Field>
            <Field label="Last name">
              <TextInput required value={parent.lastName} onChange={(e) => setParent({ ...parent, lastName: e.target.value })} autoComplete="off" />
            </Field>
            <Field label="Mobile number" hint="So we can reach them during the service.">
              <TextInput required type="tel" value={parent.mobile} onChange={(e) => setParent({ ...parent, mobile: e.target.value })} autoComplete="off" />
            </Field>
          </div>
        </Card>

        {children.map((c, i) => (
          <Card
            key={i}
            title={`Child ${children.length > 1 ? i + 1 : ''}`.trim()}
            actions={
              children.length > 1 && (
                <Button variant="ghost" type="button" onClick={() => setChildren(children.filter((_, j) => j !== i))}>
                  Remove
                </Button>
              )
            }
          >
            <div className="form-grid">
              <Field label="First name">
                <TextInput required value={c.firstName} onChange={(e) => update(i, { firstName: e.target.value })} autoComplete="off" />
              </Field>
              <Field label="Last name" hint={parent.lastName && !c.lastName ? `Leave empty for ${parent.lastName}` : undefined}>
                <TextInput value={c.lastName} onChange={(e) => update(i, { lastName: e.target.value })} autoComplete="off" />
              </Field>
              <Field label="Date of birth" hint="Decides their class.">
                <TextInput required type="date" value={c.dateOfBirth} onChange={(e) => update(i, { dateOfBirth: e.target.value })} />
              </Field>
              <Field label="Allergies">
                <TextInput value={c.allergies} onChange={(e) => update(i, { allergies: e.target.value })} maxLength={500} placeholder="None" />
              </Field>
              <Field label="Medical needs">
                <TextInput value={c.medical} onChange={(e) => update(i, { medical: e.target.value })} maxLength={500} placeholder="None" />
              </Field>
              <Field label="Anything else the team should know">
                <TextInput value={c.other} onChange={(e) => update(i, { other: e.target.value })} maxLength={500} />
              </Field>
            </div>
          </Card>
        ))}

        <div className="row">
          <Button type="button" onClick={() => setChildren([...children, emptyChild()])}>
            Add another child
          </Button>
        </div>

        <Card>
          <label className="checkbox">
            <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
            <span>The parent agrees that Shapers Church may keep their contact details and their children’s details, including any care notes, for kids church.</span>
          </label>
          <ErrorNote error={checkIn.error} />
          <div className="row">
            <Button type="submit" variant="primary" busy={checkIn.isPending} disabled={!consent}>
              Check in
            </Button>
          </div>
        </Card>
      </form>
    </>
  );
}

/** The classes and their age ranges. A child goes to the narrowest class that fits their age. */
export function KidsClassesPage() {
  const queryClient = useQueryClient();
  const classes = useQuery({ queryKey: ['kids', 'classes'], queryFn: async () => unwrap(await api.GET('/api/admin/kids/classes')) });
  const done = () => void queryClient.invalidateQueries({ queryKey: ['kids'] });
  const [form, setForm] = useState({ name: '', fromAge: '0', toAge: '2' });
  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/kids/classes', { body: { name: form.name, fromAge: Number(form.fromAge), toAge: Number(form.toAge) } })),
    onSuccess: () => {
      setForm({ name: '', fromAge: '0', toAge: '2' });
      done();
    },
  });

  return (
    <>
      <PageHeader title="Kids classes" subtitle="Each child goes to the class for their age. Where classes overlap, the narrowest one wins, so a special class can sit inside a general one." />
      {classes.isPending && <Loading />}
      <ErrorNote error={classes.error} />
      {classes.data && (
        <Card>
          {classes.data.length === 0 && <Empty>No classes yet. Add the first one below.</Empty>}
          <ul className="list">
            {classes.data.map((c) => (
              <ClassRow key={c.id} kidsClass={c} onChanged={done} />
            ))}
          </ul>
        </Card>
      )}
      <Card title="Add a class">
        <form
          className="form-grid"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <Field label="Name">
            <TextInput required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} maxLength={80} />
          </Field>
          <Field label="From age">
            <TextInput required type="number" min={0} max={17} value={form.fromAge} onChange={(e) => setForm({ ...form, fromAge: e.target.value })} />
          </Field>
          <Field label="To age">
            <TextInput required type="number" min={0} max={17} value={form.toAge} onChange={(e) => setForm({ ...form, toAge: e.target.value })} />
          </Field>
          <div className="form-actions">
            <ErrorNote error={create.error} />
            <Button type="submit" variant="primary" busy={create.isPending}>
              Add class
            </Button>
          </div>
        </form>
      </Card>
    </>
  );
}

function ClassRow({ kidsClass: c, onChanged }: { kidsClass: Schemas['KidsClassDto']; onChanged: () => void }) {
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState({ name: c.name, fromAge: String(c.fromAge), toAge: String(c.toAge) });
  const save = useMutation({
    mutationFn: async () =>
      unwrap(await api.PUT('/api/admin/kids/classes/{id}', { params: { path: { id: c.id } }, body: { name: form.name, fromAge: Number(form.fromAge), toAge: Number(form.toAge) } })),
    onSuccess: () => {
      setEditing(false);
      onChanged();
    },
  });
  const toggle = useMutation({
    mutationFn: async () =>
      c.isArchived
        ? unwrap(await api.POST('/api/admin/kids/classes/{id}/restore', { params: { path: { id: c.id } } }))
        : unwrap(await api.POST('/api/admin/kids/classes/{id}/archive', { params: { path: { id: c.id } } })),
    onSuccess: onChanged,
  });

  if (editing) {
    return (
      <li className="list-row">
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          <TextInput aria-label="Name" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} maxLength={80} />
          <TextInput aria-label="From age" type="number" min={0} max={17} value={form.fromAge} onChange={(e) => setForm({ ...form, fromAge: e.target.value })} className="input minutes-input" />
          <TextInput aria-label="To age" type="number" min={0} max={17} value={form.toAge} onChange={(e) => setForm({ ...form, toAge: e.target.value })} className="input minutes-input" />
          <Button type="submit" variant="primary" busy={save.isPending}>
            Save
          </Button>
          <Button type="button" variant="ghost" onClick={() => setEditing(false)}>
            Cancel
          </Button>
        </form>
        <ErrorNote error={save.error} />
      </li>
    );
  }

  return (
    <li className={`list-row${c.isArchived ? ' kids-collected' : ''}`}>
      <span>
        <strong>{c.name}</strong> <span className="muted">ages {c.fromAge}–{c.toAge}</span>
        {c.isArchived && <Badge>Not in use</Badge>}
      </span>
      <span className="row">
        <Button variant="ghost" onClick={() => setEditing(true)}>
          Edit
        </Button>
        <Button
          variant="ghost"
          busy={toggle.isPending}
          onClick={() => (c.isArchived || window.confirm(`Stop using ${c.name}? Children its age go to another class that fits, or can't be checked in until one does.`)) && toggle.mutate()}
        >
          {c.isArchived ? 'Use again' : 'Stop using'}
        </Button>
      </span>
    </li>
  );
}
