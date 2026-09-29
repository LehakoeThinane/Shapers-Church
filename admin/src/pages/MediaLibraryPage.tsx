import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { Button, Card, Empty, ErrorNote, Field, PageHeader, TextInput } from '../components/ui';
import { api, formatDate, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { uploadFile } from '../lib/upload';

/** Series and speakers: the building blocks the sermon editor picks from. */
export function MediaLibraryPage() {
  const { data: access } = useAccess();
  return (
    <>
      <PageHeader title="Series and speakers" subtitle={<Link to="/sermons">Back to sermons</Link>} />
      <div className="grid-2">
        <SeriesCard />
        {can(access, Permissions.speakersManage) ? <SpeakersCard /> : <Card title="Speakers"><Empty>You can't manage speakers.</Empty></Card>}
      </div>
    </>
  );
}

function ImagePicker({ label, onUploaded }: { label: string; onUploaded: (asset: Schemas['AssetDto']) => void }) {
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  return (
    <div className="stack">
      {progress !== null ? (
        <progress max={1} value={progress} />
      ) : (
        <label className="btn btn-secondary">
          {label}
          <input
            type="file"
            hidden
            accept="image/jpeg,image/png,image/webp"
            onChange={async (e) => {
              const file = e.target.files?.[0];
              if (!file) return;
              setError(null);
              setProgress(0);
              try {
                onUploaded(await uploadFile('Image', file, setProgress));
              } catch (err) {
                setError(err);
              } finally {
                setProgress(null);
              }
            }}
          />
        </label>
      )}
      <ErrorNote error={error} />
    </div>
  );
}

function SeriesCard() {
  const queryClient = useQueryClient();
  const series = useQuery({ queryKey: ['admin-series'], queryFn: async () => unwrap(await api.GET('/api/admin/media/series')) });
  const [form, setForm] = useState({ title: '', description: '', startsOn: '', artwork: null as Schemas['AssetDto'] | null });

  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/media/series', {
          body: {
            title: form.title,
            description: form.description || null,
            startsOn: form.startsOn || null,
            endsOn: null,
            artworkAssetId: form.artwork?.id ?? null,
          },
        }),
      ),
    onSuccess: () => {
      setForm({ title: '', description: '', startsOn: '', artwork: null });
      void queryClient.invalidateQueries({ queryKey: ['admin-series'] });
    },
  });

  return (
    <Card title="Series">
      <ErrorNote error={series.error} />
      {series.data?.length === 0 && <Empty>No series yet.</Empty>}
      <ul className="list">
        {series.data?.map((s) => (
          <li key={s.id} className="list-row">
            <span className="row">
              {s.artworkUrl && <img className="thumb-sm" src={s.artworkUrl} alt="" width={48} height={48} />}
              <span>
                {s.title}
                <br />
                <span className="small muted">
                  {s.sermonCount} {s.sermonCount === 1 ? 'sermon' : 'sermons'}
                  {s.startsOn && ` · from ${formatDate(s.startsOn)}`}
                </span>
              </span>
            </span>
          </li>
        ))}
      </ul>

      <form
        className="stack"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          create.mutate();
        }}>
        <strong className="small">New series</strong>
        <Field label="Title">
          <TextInput required maxLength={150} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
        </Field>
        <Field label="Description">
          <textarea className="input" rows={2} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </Field>
        <Field label="Starts on">
          <TextInput type="date" value={form.startsOn} onChange={(e) => setForm({ ...form, startsOn: e.target.value })} />
        </Field>
        {form.artwork ? (
          <img className="thumb" src={form.artwork.url} alt="Series artwork" width={160} height={160} />
        ) : (
          <ImagePicker label="Add artwork" onUploaded={(artwork) => setForm({ ...form, artwork })} />
        )}
        <ErrorNote error={create.error} />
        <Button variant="primary" type="submit" busy={create.isPending}>
          Add series
        </Button>
      </form>
    </Card>
  );
}

function SpeakersCard() {
  const queryClient = useQueryClient();
  const speakers = useQuery({ queryKey: ['speakers'], queryFn: async () => unwrap(await api.GET('/api/admin/media/speakers')) });
  const [form, setForm] = useState({ name: '', title: '', photo: null as Schemas['AssetDto'] | null });

  const create = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/admin/media/speakers', {
          body: { name: form.name, title: form.title || null, bio: null, personId: null, photoAssetId: form.photo?.id ?? null },
        }),
      ),
    onSuccess: () => {
      setForm({ name: '', title: '', photo: null });
      void queryClient.invalidateQueries({ queryKey: ['speakers'] });
    },
  });

  return (
    <Card title="Speakers">
      <ErrorNote error={speakers.error} />
      {speakers.data?.length === 0 && <Empty>No speakers yet.</Empty>}
      <ul className="list">
        {speakers.data?.map((s) => (
          <li key={s.id} className="list-row">
            <span className="row">
              {s.photoUrl && <img className="thumb-sm round" src={s.photoUrl} alt="" width={40} height={40} />}
              <span>
                {s.name} {s.title && <span className="small muted">{s.title}</span>}
              </span>
            </span>
          </li>
        ))}
      </ul>
      <form
        className="stack"
        onSubmit={(e: FormEvent) => {
          e.preventDefault();
          create.mutate();
        }}>
        <strong className="small">New speaker</strong>
        <Field label="Name">
          <TextInput required maxLength={120} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </Field>
        <Field label="Title" hint="e.g. Senior Pastor">
          <TextInput maxLength={120} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
        </Field>
        {form.photo ? (
          <img className="thumb-sm round" src={form.photo.url} alt="Speaker photo" width={64} height={64} />
        ) : (
          <ImagePicker label="Add photo" onUploaded={(photo) => setForm({ ...form, photo })} />
        )}
        <ErrorNote error={create.error} />
        <Button variant="primary" type="submit" busy={create.isPending}>
          Add speaker
        </Button>
      </form>
    </Card>
  );
}
