import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, TextInput } from '../components/ui';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';

type Breach = Schemas['BreachDto'];
type Save = Schemas['SaveBreachRequest'];

const toLocalInput = (iso: string | null | undefined) => {
  if (!iso) return '';
  const d = new Date(iso);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
};
const fromLocalInput = (value: string) => (value ? new Date(value).toISOString() : null);

/**
 * The breach register (POPIA section 22). Record anything that may have exposed personal information, what was done,
 * and when the Information Regulator and the people affected were told.
 */
export function BreachesPage() {
  const breaches = useQuery({ queryKey: ['breaches'], queryFn: async () => unwrap(await api.GET('/api/admin/privacy/breaches')) });
  const [adding, setAdding] = useState(false);

  return (
    <>
      <PageHeader
        title="Breach register"
        subtitle="If personal information may have been lost, stolen or seen by someone who shouldn't have it, record it here straight away."
        actions={
          !adding && (
            <Button variant="primary" onClick={() => setAdding(true)}>
              Record a breach
            </Button>
          )
        }
      />
      <div className="stack">
        {adding && <BreachForm onDone={() => setAdding(false)} />}
        <ErrorNote error={breaches.error} />
        {breaches.isPending ? (
          <Loading />
        ) : breaches.data?.length === 0 ? (
          !adding && (
            <Card>
              <Empty>No breaches recorded.</Empty>
            </Card>
          )
        ) : (
          breaches.data?.map((b) => <BreachForm key={`${b.id}-${b.updatedAt}`} existing={b} />)
        )}
      </div>
    </>
  );
}

function BreachForm({ existing, onDone }: { existing?: Breach; onDone?: () => void }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(!existing);
  const [form, setForm] = useState(() => ({
    title: existing?.title ?? '',
    description: existing?.description ?? '',
    discoveredAt: toLocalInput(existing?.discoveredAt ?? new Date().toISOString()),
    occurredAt: toLocalInput(existing?.occurredAt),
    dataInvolved: existing?.dataInvolved ?? '',
    peopleAffected: existing?.peopleAffected?.toString() ?? '',
    specialInformation: existing?.specialInformation ?? false,
    containment: existing?.containment ?? '',
    regulatorNotifiedAt: toLocalInput(existing?.regulatorNotifiedAt),
    peopleNotifiedAt: toLocalInput(existing?.peopleNotifiedAt),
  }));
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['breaches'] });

  const body = (): Save => ({
    title: form.title,
    description: form.description,
    discoveredAt: fromLocalInput(form.discoveredAt)!,
    occurredAt: fromLocalInput(form.occurredAt),
    dataInvolved: form.dataInvolved || null,
    peopleAffected: form.peopleAffected ? Number(form.peopleAffected) : null,
    specialInformation: form.specialInformation,
    containment: form.containment || null,
    regulatorNotifiedAt: fromLocalInput(form.regulatorNotifiedAt),
    peopleNotifiedAt: fromLocalInput(form.peopleNotifiedAt),
  });
  const save = useMutation({
    mutationFn: async () =>
      existing
        ? unwrap(await api.PUT('/api/admin/privacy/breaches/{id}', { params: { path: { id: existing.id } }, body: body() }))
        : unwrap(await api.POST('/api/admin/privacy/breaches', { body: body() })),
    onSuccess: () => {
      refresh();
      onDone?.();
    },
  });
  const status = useMutation({
    mutationFn: async (action: 'close' | 'reopen') => {
      const path = { params: { path: { id: existing!.id } } };
      return action === 'close'
        ? unwrap(await api.POST('/api/admin/privacy/breaches/{id}/close', path))
        : unwrap(await api.POST('/api/admin/privacy/breaches/{id}/reopen', path));
    },
    onSuccess: refresh,
  });

  const title = existing ? (
    <span className="row">
      {existing.title}
      <Badge tone={existing.status === 'Closed' ? 'success' : 'accent'}>{existing.status}</Badge>
      {existing.notificationOverdue && <Badge tone="danger">Regulator not told</Badge>}
      {existing.specialInformation && <Badge tone="danger">Special information</Badge>}
    </span>
  ) : (
    'New breach'
  );

  if (existing && !open) {
    return (
      <Card title={title} actions={<Button variant="ghost" onClick={() => setOpen(true)}>Open</Button>}>
        <p className="small muted">
          Discovered {formatDateTime(existing.discoveredAt)}
          {existing.peopleAffected != null && ` · ${existing.peopleAffected} people affected`}
          {existing.regulatorNotifiedAt && ` · Regulator told ${formatDateTime(existing.regulatorNotifiedAt)}`}
        </p>
      </Card>
    );
  }

  return (
    <Card title={title} actions={existing && <Button variant="ghost" onClick={() => setOpen(false)}>Close panel</Button>}>
      <form
        className="stack"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          save.mutate();
        }}>
        {existing?.notificationOverdue && (
          <p className="small">
            More than 72 hours have passed since this was discovered. POPIA requires telling the Information Regulator and the people affected as soon
            as reasonably possible.
          </p>
        )}
        <Field label="Title">
          <TextInput required maxLength={150} value={form.title} onChange={(e) => set({ title: e.target.value })} />
        </Field>
        <Field label="What happened">
          <textarea className="input" rows={3} required maxLength={4000} value={form.description} onChange={(e) => set({ description: e.target.value })} />
        </Field>
        <div className="row">
          <Field label="Discovered">
            <TextInput type="datetime-local" required value={form.discoveredAt} onChange={(e) => set({ discoveredAt: e.target.value })} />
          </Field>
          <Field label="Happened (if known)">
            <TextInput type="datetime-local" value={form.occurredAt} onChange={(e) => set({ occurredAt: e.target.value })} />
          </Field>
        </div>
        <Field label="Information involved" hint="What kind of information, about whom.">
          <textarea className="input" rows={2} maxLength={4000} value={form.dataInvolved} onChange={(e) => set({ dataInvolved: e.target.value })} />
        </Field>
        <div className="row">
          <Field label="People affected">
            <TextInput type="number" min={0} value={form.peopleAffected} onChange={(e) => set({ peopleAffected: e.target.value })} />
          </Field>
          <label className="checkbox">
            <input type="checkbox" checked={form.specialInformation} onChange={(e) => set({ specialInformation: e.target.checked })} />
            Religious, health or children&rsquo;s information involved
          </label>
        </div>
        <Field label="What was done to contain it">
          <textarea className="input" rows={2} maxLength={4000} value={form.containment} onChange={(e) => set({ containment: e.target.value })} />
        </Field>
        <div className="row">
          <Field label="Information Regulator told" hint="POPIA section 22.">
            <TextInput type="datetime-local" value={form.regulatorNotifiedAt} onChange={(e) => set({ regulatorNotifiedAt: e.target.value })} />
          </Field>
          <Field label="People affected told">
            <TextInput type="datetime-local" value={form.peopleNotifiedAt} onChange={(e) => set({ peopleNotifiedAt: e.target.value })} />
          </Field>
        </div>
        <ErrorNote error={save.error ?? status.error} />
        <div className="row">
          <Button variant="primary" type="submit" busy={save.isPending}>
            {existing ? 'Save' : 'Record breach'}
          </Button>
          {existing?.status === 'Open' && (
            <Button busy={status.isPending} onClick={() => status.mutate('close')}>
              Close breach
            </Button>
          )}
          {existing?.status === 'Closed' && (
            <Button busy={status.isPending} onClick={() => status.mutate('reopen')}>
              Reopen
            </Button>
          )}
          {!existing && onDone && (
            <Button variant="ghost" onClick={onDone}>
              Cancel
            </Button>
          )}
        </div>
      </form>
    </Card>
  );
}
