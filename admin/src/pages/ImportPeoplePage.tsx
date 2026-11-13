import { useQueryClient } from '@tanstack/react-query';
import { useState, type ChangeEvent } from 'react';
import { Link } from 'react-router';
import { Button, Card, ErrorNote, Field, PageHeader, Select } from '../components/ui';
import { api, unwrap, type Schemas } from '../lib/api';
import { readPeopleCsv } from '../lib/peopleImport';
import { useCampuses, useMembershipStatuses } from '../lib/queries';

export function ImportPeoplePage() {
  const queryClient = useQueryClient();
  const { data: campuses } = useCampuses();
  const { data: statuses } = useMembershipStatuses();
  const [fileName, setFileName] = useState('');
  const [rows, setRows] = useState<Schemas['ImportPersonRow'][]>([]);
  const [campusId, setCampusId] = useState('');
  const [statusId, setStatusId] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<Schemas['ImportPeopleResult'] | null>(null);

  const choose = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    setResult(null);
    setError(null);
    setRows([]);
    setFileName(file?.name ?? '');
    if (!file) return;
    const read = readPeopleCsv(await file.text());
    if (read.error) setError(new Error(read.error));
    setRows(read.rows);
  };

  const importRows = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = { campusId: campusId || null, membershipStatusId: statusId || null, rows };
      setResult(unwrap(await api.POST('/api/admin/people/import', { body })));
      setRows([]);
      await queryClient.invalidateQueries({ queryKey: ['people'] });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Import people"
        subtitle="Upload a CSV export, such as Planning Center's People Export. Anyone already here is left as they are."
      />
      <Card>
        <div className="form-grid">
          <Field label="CSV file" hint="Needs First Name and Last Name columns. Emails, phone numbers and birthdates come across too.">
            <input type="file" accept=".csv,text/csv" onChange={choose} />
          </Field>
          <Field label="Campus">
            <Select value={campusId} onChange={(e) => setCampusId(e.target.value)}>
              <option value="">Primary campus</option>
              {campuses?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Membership status">
            <Select value={statusId} onChange={(e) => setStatusId(e.target.value)}>
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
            <Button variant="primary" disabled={rows.length === 0} busy={busy} onClick={importRows}>
              {rows.length > 0 ? `Import ${rows.length} ${rows.length === 1 ? 'person' : 'people'}` : 'Import'}
            </Button>
          </div>
        </div>

        {rows.length > 0 && (
          <table className="table">
            <caption className="muted small">Preview of {fileName}</caption>
            <thead>
              <tr>
                <th>Row</th>
                <th>Name</th>
                <th>Birthdate</th>
                <th>Email</th>
                <th>Phone</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.rowNumber}>
                  <td>{r.rowNumber}</td>
                  <td>{`${r.firstName} ${r.lastName}`.trim() || '—'}</td>
                  <td>{r.dateOfBirth ?? '—'}</td>
                  <td>{r.emails.join(', ') || '—'}</td>
                  <td>{r.mobiles.join(', ') || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {result && (
          <div className="stack">
            <p>
              Added {result.added} {result.added === 1 ? 'person' : 'people'}
              {result.alreadyThere > 0 && `; ${result.alreadyThere} already here, left as they were`}.{' '}
              <Link to="/people">See everyone</Link> or <Link to="/duplicates">check possible duplicates</Link>.
            </p>
            {(result.skipped.length > 0 || result.warnings.length > 0) && (
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Name</th>
                    <th>Note</th>
                  </tr>
                </thead>
                <tbody>
                  {result.skipped.map((n) => (
                    <tr key={`s${n.rowNumber}`}>
                      <td>{n.rowNumber}</td>
                      <td>{n.name || '—'}</td>
                      <td>Not imported: {n.message}</td>
                    </tr>
                  ))}
                  {result.warnings.map((n, i) => (
                    <tr key={`w${n.rowNumber}-${i}`}>
                      <td>{n.rowNumber}</td>
                      <td>{n.name}</td>
                      <td>{n.message}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </Card>
    </>
  );
}
