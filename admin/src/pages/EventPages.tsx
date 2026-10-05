import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import QrScanner from 'qr-scanner';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, apiBaseUrl, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';

type AdminEvent = Schemas['EventAdminDto'];
type SaveEvent = Schemas['SaveEventRequest'];
type CheckInResult = Schemas['CheckInResultDto'];

const statusTone = { Draft: 'neutral', Published: 'success', Cancelled: 'danger' } as const;

const toLocalInput = (iso: string | null | undefined) => {
  if (!iso) return '';
  const d = new Date(iso);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
};
const fromLocalInput = (value: string) => (value ? new Date(value).toISOString() : null);

function seatsLabel(e: AdminEvent) {
  if (!e.event.registrationRequired) return 'No registration';
  const booked = `${e.confirmed} booked`;
  return e.capacity ? `${booked} of ${e.capacity}${e.waitlisted ? ` · ${e.waitlisted} waiting` : ''}` : booked;
}

export function EventsPage() {
  const events = useQuery({ queryKey: ['events'], queryFn: async () => unwrap(await api.GET('/api/admin/events')) });
  const { data: access } = useAccess();

  return (
    <>
      <PageHeader
        title="Events"
        subtitle="Plan events, take registrations and check people in at the door."
        actions={
          can(access, Permissions.eventsEdit) && (
            <Link className="btn btn-primary" to="/events/new">
              New event
            </Link>
          )
        }
      />
      <Card>
        <ErrorNote error={events.error} />
        {events.isPending ? (
          <Loading />
        ) : events.data?.length === 0 ? (
          <Empty>No events yet.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Event</th>
                <th>When</th>
                <th>Registrations</th>
                <th>Status</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {events.data?.map((e) => (
                <tr key={e.event.id}>
                  <td>
                    <Link to={`/events/${e.event.id}`}>{e.event.title}</Link>
                    {e.event.visibility === 'Members' && <span className="muted small"> · members only</span>}
                  </td>
                  <td>{formatDateTime(e.event.startsAt)}</td>
                  <td>{seatsLabel(e)}</td>
                  <td>
                    <Badge tone={statusTone[e.status]}>{e.status}</Badge>
                  </td>
                  <td className="row">
                    {e.event.registrationRequired && can(access, Permissions.eventsRegistrationsView) && <Link to={`/events/${e.event.id}/attendees`}>Attendees</Link>}
                    {e.status === 'Published' && can(access, Permissions.eventsCheckIn) && <Link to={`/events/${e.event.id}/check-in`}>Check-in</Link>}
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

export function EventEditorPage() {
  const { id } = useParams();
  const event = useQuery({
    queryKey: ['event', id],
    enabled: !!id,
    queryFn: async () => unwrap(await api.GET('/api/admin/events/{id}', { params: { path: { id: id! } } })),
  });

  if (!id) return <EventEditor key="new" />;
  if (event.isPending) return <Loading />;
  if (event.error || !event.data) return <ErrorNote error={event.error ?? new Error('Not found')} />;
  return <EventEditor key={event.data.event.id} existing={event.data} />;
}

type Form = {
  title: string;
  summary: string;
  description: string;
  startsAt: string;
  endsAt: string;
  locationName: string;
  locationAddress: string;
  imageUrl: string;
  visibility: 'Public' | 'Members';
  registrationRequired: boolean;
  opensAt: string;
  closesAt: string;
  capacity: string;
  waitlistEnabled: boolean;
  maxPerRegistration: string;
  questions: { id: string | null; label: string; required: boolean }[];
};

function initialForm(e?: AdminEvent): Form {
  if (!e) {
    const start = new Date();
    start.setDate(start.getDate() + 14);
    start.setHours(18, 0, 0, 0);
    const iso = start.toISOString();
    return {
      title: '',
      summary: '',
      description: '',
      startsAt: toLocalInput(iso),
      endsAt: toLocalInput(new Date(start.getTime() + 2 * 3_600_000).toISOString()),
      locationName: 'Shapers Church',
      locationAddress: '8 Mellis Road, Rivonia, Sandton',
      imageUrl: '',
      visibility: 'Public',
      registrationRequired: true,
      opensAt: '',
      closesAt: '',
      capacity: '',
      waitlistEnabled: false,
      maxPerRegistration: '4',
      questions: [],
    };
  }
  return {
    title: e.event.title,
    summary: e.event.summary ?? '',
    description: e.event.description ?? '',
    startsAt: toLocalInput(e.event.startsAt),
    endsAt: toLocalInput(e.event.endsAt),
    locationName: e.event.location?.name ?? '',
    locationAddress: e.event.location?.address ?? '',
    imageUrl: e.event.imageUrl ?? '',
    visibility: e.event.visibility,
    registrationRequired: e.event.registrationRequired,
    opensAt: toLocalInput(e.registrationOpensAt),
    closesAt: toLocalInput(e.registrationClosesAt),
    capacity: e.capacity?.toString() ?? '',
    waitlistEnabled: e.waitlistEnabled,
    maxPerRegistration: e.event.maxPerRegistration.toString(),
    questions: e.event.questions.map((q) => ({ id: q.id, label: q.label, required: q.required })),
  };
}

function toRequest(f: Form, scope: string | null): SaveEvent {
  const capacity = f.capacity ? Number(f.capacity) : null;
  return {
    title: f.title,
    summary: f.summary || null,
    description: f.description || null,
    startsAt: fromLocalInput(f.startsAt)!,
    endsAt: fromLocalInput(f.endsAt)!,
    location: f.locationName ? { name: f.locationName, address: f.locationAddress || null } : null,
    imageUrl: f.imageUrl || null,
    visibility: f.visibility,
    registrationRequired: f.registrationRequired,
    registrationOpensAt: fromLocalInput(f.opensAt),
    registrationClosesAt: fromLocalInput(f.closesAt),
    capacity,
    waitlistEnabled: capacity !== null && f.waitlistEnabled,
    maxPerRegistration: Number(f.maxPerRegistration) || 1,
    questions: f.questions.filter((q) => q.label.trim()),
    scope,
  };
}

function EventEditor({ existing }: { existing?: AdminEvent }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const [form, setForm] = useState<Form>(() => initialForm(existing));
  const set = (patch: Partial<Form>) => setForm((f) => ({ ...f, ...patch }));
  const saved = (e: AdminEvent) => {
    queryClient.setQueryData(['event', e.event.id], e);
    void queryClient.invalidateQueries({ queryKey: ['events'] });
  };

  const save = useMutation({
    mutationFn: async () => {
      const body = toRequest(form, existing?.scope ?? null);
      return existing
        ? unwrap(await api.PUT('/api/admin/events/{id}', { params: { path: { id: existing.event.id } }, body }))
        : unwrap(await api.POST('/api/admin/events', { body }));
    },
    onSuccess: (e) => {
      saved(e);
      if (!existing) navigate(`/events/${e.event.id}`, { replace: true });
    },
  });
  const status = useMutation({
    mutationFn: async (action: 'publish' | 'unpublish' | 'cancel') => {
      const path = { params: { path: { id: existing!.event.id } } };
      if (action === 'publish') return unwrap(await api.POST('/api/admin/events/{id}/publish', path));
      if (action === 'unpublish') return unwrap(await api.POST('/api/admin/events/{id}/unpublish', path));
      return unwrap(await api.POST('/api/admin/events/{id}/cancel', path));
    },
    onSuccess: saved,
  });

  const editable = can(access, Permissions.eventsEdit) && existing?.status !== 'Cancelled';
  const canPublish = can(access, Permissions.eventsPublish);

  return (
    <>
      <PageHeader
        title={existing ? existing.event.title : 'New event'}
        subtitle={
          <>
            <Link to="/events">All events</Link>
            {existing && (
              <>
                {' · '}
                {seatsLabel(existing)}
                {existing.checkedIn > 0 && ` · ${existing.checkedIn} checked in`}
              </>
            )}
          </>
        }
        actions={existing && <Badge tone={statusTone[existing.status]}>{existing.status}</Badge>}
      />

      <form
        className="grid-2"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          save.mutate();
        }}>
        <div className="stack">
          <Card title="Details">
            <fieldset className="stack" disabled={!editable}>
              <Field label="Title">
                <TextInput required maxLength={150} value={form.title} onChange={(e) => set({ title: e.target.value })} />
              </Field>
              <Field label="Summary" hint="One or two lines for the events list.">
                <textarea className="input" rows={2} maxLength={300} value={form.summary} onChange={(e) => set({ summary: e.target.value })} />
              </Field>
              <Field label="Description" hint="Markdown is supported.">
                <textarea className="input" rows={6} maxLength={5000} value={form.description} onChange={(e) => set({ description: e.target.value })} />
              </Field>
              <div className="row">
                <Field label="Starts">
                  <TextInput type="datetime-local" required value={form.startsAt} onChange={(e) => set({ startsAt: e.target.value })} />
                </Field>
                <Field label="Ends">
                  <TextInput type="datetime-local" required value={form.endsAt} onChange={(e) => set({ endsAt: e.target.value })} />
                </Field>
              </div>
              <Field label="Venue">
                <TextInput maxLength={150} value={form.locationName} onChange={(e) => set({ locationName: e.target.value })} />
              </Field>
              <Field label="Address">
                <TextInput maxLength={300} value={form.locationAddress} onChange={(e) => set({ locationAddress: e.target.value })} />
              </Field>
              <Field label="Image link" hint="Optional. A wide image (16:9) looks best.">
                <TextInput type="url" maxLength={500} value={form.imageUrl} onChange={(e) => set({ imageUrl: e.target.value })} />
              </Field>
              <Field label="Who can see it">
                <Select value={form.visibility} onChange={(e) => set({ visibility: e.target.value as Form['visibility'] })}>
                  <option value="Public">Everyone (app and website)</option>
                  <option value="Members">Signed-in members only</option>
                </Select>
              </Field>
            </fieldset>
          </Card>
        </div>

        <div className="stack">
          <Card title="Registration">
            <fieldset className="stack" disabled={!editable}>
              <label className="checkbox">
                <input type="checkbox" checked={form.registrationRequired} onChange={(e) => set({ registrationRequired: e.target.checked })} />
                People register for this event
              </label>
              {form.registrationRequired && (
                <>
                  <div className="row">
                    <Field label="Seats" hint="Leave empty for no limit.">
                      <TextInput type="number" min={1} max={100000} value={form.capacity} onChange={(e) => set({ capacity: e.target.value })} />
                    </Field>
                    <Field label="Most per booking">
                      <TextInput type="number" min={1} max={10} required value={form.maxPerRegistration} onChange={(e) => set({ maxPerRegistration: e.target.value })} />
                    </Field>
                  </div>
                  <label className="checkbox">
                    <input type="checkbox" disabled={!form.capacity} checked={!!form.capacity && form.waitlistEnabled} onChange={(e) => set({ waitlistEnabled: e.target.checked })} />
                    Waiting list when full (people move up in order when someone cancels)
                  </label>
                  <div className="row">
                    <Field label="Opens" hint="Empty: as soon as it's published.">
                      <TextInput type="datetime-local" value={form.opensAt} onChange={(e) => set({ opensAt: e.target.value })} />
                    </Field>
                    <Field label="Closes" hint="Empty: when the event starts.">
                      <TextInput type="datetime-local" value={form.closesAt} onChange={(e) => set({ closesAt: e.target.value })} />
                    </Field>
                  </div>
                  <Field label="Questions" hint="Up to five, e.g. dietary needs. Only ask what you need.">
                    <div className="stack">
                      {form.questions.map((q, i) => (
                        <div key={q.id ?? `new-${i}`} className="row">
                          <TextInput
                            maxLength={200}
                            value={q.label}
                            placeholder="Question"
                            onChange={(e) => set({ questions: form.questions.map((x, j) => (j === i ? { ...x, label: e.target.value } : x)) })}
                          />
                          <label className="checkbox">
                            <input
                              type="checkbox"
                              checked={q.required}
                              onChange={(e) => set({ questions: form.questions.map((x, j) => (j === i ? { ...x, required: e.target.checked } : x)) })}
                            />
                            Required
                          </label>
                          <Button variant="ghost" type="button" onClick={() => set({ questions: form.questions.filter((_, j) => j !== i) })}>
                            Remove
                          </Button>
                        </div>
                      ))}
                      {form.questions.length < 5 && (
                        <Button variant="ghost" type="button" onClick={() => set({ questions: [...form.questions, { id: null, label: '', required: false }] })}>
                          Add a question
                        </Button>
                      )}
                    </div>
                  </Field>
                </>
              )}
            </fieldset>
          </Card>

          <Card title="Save and publish">
            <div className="stack">
              <ErrorNote error={save.error ?? status.error} />
              {editable && (
                <Button variant="primary" type="submit" busy={save.isPending}>
                  {existing ? 'Save changes' : 'Create draft'}
                </Button>
              )}
              {existing && canPublish && existing.status === 'Draft' && (
                <Button type="button" busy={status.isPending} onClick={() => status.mutate('publish')}>
                  Publish
                </Button>
              )}
              {existing && canPublish && existing.status === 'Published' && (
                <>
                  <Button type="button" variant="ghost" busy={status.isPending} disabled={existing.confirmed + existing.waitlisted > 0} onClick={() => status.mutate('unpublish')}>
                    Unpublish
                  </Button>
                  <Button
                    type="button"
                    variant="danger"
                    busy={status.isPending}
                    onClick={() => confirm('Cancel this event? Everyone registered will be emailed.') && status.mutate('cancel')}>
                    Cancel event
                  </Button>
                </>
              )}
              {existing && existing.status === 'Published' && existing.confirmed + existing.waitlisted > 0 && (
                <p className="small muted">People have registered, so it can't be unpublished. Cancel it instead if it isn't happening.</p>
              )}
            </div>
          </Card>
        </div>
      </form>
    </>
  );
}

export function EventAttendeesPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const path = { params: { path: { id } } };
  const event = useQuery({ queryKey: ['event', id], queryFn: async () => unwrap(await api.GET('/api/admin/events/{id}', path)) });
  const attendees = useQuery({ queryKey: ['event', id, 'attendees'], queryFn: async () => unwrap(await api.GET('/api/admin/events/{id}/attendees', path)) });
  const [filter, setFilter] = useState('');
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['event', id] });
    void queryClient.invalidateQueries({ queryKey: ['events'] });
  };
  const cancel = useMutation({
    mutationFn: async (registrationId: string) =>
      unwrap(await api.POST('/api/admin/events/registrations/{registrationId}/cancel', { params: { path: { registrationId } } })),
    onSuccess: refresh,
  });

  const canManage = can(access, Permissions.eventsRegistrationsManage);
  const rows = (attendees.data ?? []).filter((r) => !filter || `${r.name} ${r.registrantName} ${r.registrantEmail ?? ''}`.toLowerCase().includes(filter.toLowerCase()));
  const questions = event.data?.event.questions ?? [];

  return (
    <>
      <PageHeader
        title={event.data ? `Attendees: ${event.data.event.title}` : 'Attendees'}
        subtitle={
          <>
            <Link to={`/events/${id}`}>Event</Link>
            {event.data && ` · ${seatsLabel(event.data)} · ${event.data.checkedIn} checked in`}
          </>
        }
        actions={
          <a className="btn btn-secondary" href={`${apiBaseUrl}/api/admin/events/${id}/attendees.csv`} download>
            Export CSV
          </a>
        }
      />
      <div className="grid-2">
        <Card title="Registrations">
          <div className="stack">
            <TextInput placeholder="Search names or email" value={filter} onChange={(e) => setFilter(e.target.value)} />
            <ErrorNote error={attendees.error ?? cancel.error} />
            {attendees.isPending ? (
              <Loading />
            ) : rows.length === 0 ? (
              <Empty>No one yet.</Empty>
            ) : (
              <table className="table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Booked by</th>
                    <th>Status</th>
                    {questions.map((q) => (
                      <th key={q.id}>{q.label}</th>
                    ))}
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.attendeeId}>
                      <td>
                        {r.personId ? <Link to={`/people/${r.personId}`}>{r.name}</Link> : r.name}
                        {r.checkedInAt && <span className="muted small"> · in {formatDateTime(r.checkedInAt)}</span>}
                      </td>
                      <td>
                        {r.registrantName}
                        <div className="muted small">
                          {r.registrantEmail} · {r.source}
                        </div>
                      </td>
                      <td>
                        <Badge tone={r.status === 'Confirmed' ? 'success' : r.status === 'Waitlisted' ? 'accent' : 'neutral'}>{r.status}</Badge>
                      </td>
                      {questions.map((q) => (
                        <td key={q.id}>{r.answers[q.label] ?? ''}</td>
                      ))}
                      <td>
                        {canManage && r.status !== 'Cancelled' && !r.checkedInAt && (
                          <Button variant="ghost" onClick={() => confirm(`Cancel the booking by ${r.registrantName}?`) && cancel.mutate(r.registrationId)}>
                            Cancel
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </Card>
        {canManage && event.data?.status === 'Published' && <PhoneInCard eventId={id} maxPerRegistration={event.data.event.maxPerRegistration} onDone={refresh} />}
      </div>
    </>
  );
}

/** Someone phoned or walked up to the info desk: book them in and record that they agreed to us keeping their details. */
function PhoneInCard({ eventId, maxPerRegistration, onDone }: { eventId: string; maxPerRegistration: number; onDone: () => void }) {
  const empty = { firstName: '', lastName: '', mobile: '', email: '', others: '', consent: false };
  const [form, setForm] = useState(empty);
  const register = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/events/{id}/registrations', {
          params: { path: { id: eventId } },
          body: {
            personId: null,
            firstName: form.firstName,
            lastName: form.lastName,
            mobile: form.mobile || null,
            email: form.email || null,
            consentGivenVerbally: form.consent,
            otherAttendeeNames: form.others
              .split('\n')
              .map((n) => n.trim())
              .filter(Boolean),
            answers: null,
          },
        }),
      ),
    onSuccess: () => {
      setForm(empty);
      onDone();
    },
  });

  return (
    <Card title="Book someone in">
      <form
        className="stack"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          register.mutate();
        }}>
        <div className="row">
          <Field label="First name">
            <TextInput required maxLength={60} value={form.firstName} onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
          </Field>
          <Field label="Last name">
            <TextInput required maxLength={60} value={form.lastName} onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
          </Field>
        </div>
        <Field label="Mobile">
          <TextInput type="tel" value={form.mobile} onChange={(e) => setForm({ ...form, mobile: e.target.value })} />
        </Field>
        <Field label="Email" hint="We send the confirmation and tickets here.">
          <TextInput type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </Field>
        {maxPerRegistration > 1 && (
          <Field label="Also coming" hint={`One name per line, up to ${maxPerRegistration - 1}.`}>
            <textarea className="input" rows={3} value={form.others} onChange={(e) => setForm({ ...form, others: e.target.value })} />
          </Field>
        )}
        <label className="checkbox">
          <input type="checkbox" checked={form.consent} onChange={(e) => setForm({ ...form, consent: e.target.checked })} />
          They agreed to Shapers Church keeping their details
        </label>
        <ErrorNote error={register.error} />
        <Button variant="primary" type="submit" busy={register.isPending}>
          Book
        </Button>
      </form>
    </Card>
  );
}

const outcomeTone = { CheckedIn: 'success', AlreadyCheckedIn: 'accent', NotConfirmed: 'danger' } as const;

/** For the door: scan tickets with the device camera (or a handheld scanner into the code box), or find someone by name. */
export function CheckInPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const event = useQuery({ queryKey: ['event', id], queryFn: async () => unwrap(await api.GET('/api/admin/events/{id}', { params: { path: { id } } })) });
  const [last, setLast] = useState<CheckInResult | null>(null);
  const [code, setCode] = useState('');
  const [search, setSearch] = useState('');
  const [scanning, setScanning] = useState(false);

  const checkIn = useMutation({
    mutationFn: async (body: { ticketCode: string | null; attendeeId: string | null }) =>
      unwrap(await api.POST('/api/admin/events/{id}/check-in', { params: { path: { id } }, body })),
    onSuccess: (result) => {
      setLast(result);
      void queryClient.invalidateQueries({ queryKey: ['event', id] });
    },
  });
  const doorList = useQuery({
    queryKey: ['event', id, 'door', search],
    enabled: search.trim().length >= 2,
    queryFn: async () => unwrap(await api.GET('/api/admin/events/{id}/door-list', { params: { path: { id }, query: { q: search.trim() } } })),
  });

  return (
    <>
      <PageHeader
        title={event.data ? `Check-in: ${event.data.event.title}` : 'Check-in'}
        subtitle={event.data && `${event.data.checkedIn} of ${event.data.confirmed} checked in`}
      />
      <div className="grid-2">
        <div className="stack">
          <Card title="Scan a ticket">
            <div className="stack">
              {scanning ? (
                <Scanner onCode={(ticketCode) => checkIn.mutate({ ticketCode, attendeeId: null })} />
              ) : (
                <Button variant="primary" onClick={() => setScanning(true)}>
                  Use the camera
                </Button>
              )}
              <form
                className="row"
                onSubmit={(e: FormEvent) => {
                  e.preventDefault();
                  if (code.trim()) checkIn.mutate({ ticketCode: code.trim(), attendeeId: null });
                  setCode('');
                }}>
                <TextInput autoFocus placeholder="Or type the ticket code" value={code} onChange={(e) => setCode(e.target.value)} />
                <Button type="submit">Check in</Button>
              </form>
            </div>
          </Card>
          <Card title="Last check-in">
            <ErrorNote error={checkIn.error} />
            {last ? (
              <div className="stack" role="status" aria-live="polite">
                <Badge tone={outcomeTone[last.outcome]}>{last.outcome === 'CheckedIn' ? 'Welcome' : last.outcome === 'AlreadyCheckedIn' ? 'Already in' : 'Not booked'}</Badge>
                <strong>{last.name}</strong>
                <span className="small muted">{last.message}</span>
              </div>
            ) : (
              <Empty>Scan a ticket to start.</Empty>
            )}
          </Card>
        </div>
        <Card title="Find by name">
          <div className="stack">
            <TextInput placeholder="Type at least two letters" value={search} onChange={(e) => setSearch(e.target.value)} />
            <ErrorNote error={doorList.error} />
            <ul className="list">
              {doorList.data?.map((t) => (
                <li key={t.attendeeId} className="list-row">
                  <span>{t.name}</span>
                  {t.checkedInAt ? (
                    <span className="small muted">In at {formatDateTime(t.checkedInAt)}</span>
                  ) : (
                    <Button onClick={() => checkIn.mutate({ ticketCode: null, attendeeId: t.attendeeId })}>Check in</Button>
                  )}
                </li>
              ))}
            </ul>
            {doorList.data?.length === 0 && <Empty>No one booked by that name.</Empty>}
          </div>
        </Card>
      </div>
    </>
  );
}

function Scanner({ onCode }: { onCode: (code: string) => void }) {
  const video = useRef<HTMLVideoElement>(null);
  const handler = useRef(onCode);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    handler.current = onCode;
  }, [onCode]);

  useEffect(() => {
    if (!video.current) return;
    // The same ticket stays in view for a while: ignore repeats for a few seconds.
    let lastCode = '';
    let lastAt = 0;
    const scanner = new QrScanner(
      video.current,
      (result) => {
        const now = Date.now();
        if (result.data === lastCode && now - lastAt < 4000) return;
        lastCode = result.data;
        lastAt = now;
        handler.current(result.data);
      },
      { returnDetailedScanResult: true, highlightScanRegion: true, preferredCamera: 'environment' },
    );
    scanner.start().catch(() => setError('The camera could not start. Allow camera access, and use the portal over HTTPS.'));
    return () => {
      scanner.stop();
      scanner.destroy();
    };
  }, []);

  return (
    <div className="stack">
      <video ref={video} className="scanner" muted playsInline />
      {error && <p className="small">{error}</p>}
    </div>
  );
}
