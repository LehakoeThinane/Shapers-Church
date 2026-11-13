import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Button, Card, ErrorNote, Loading, PageHeader } from '../components/ui';
import { api, unwrap, type Schemas } from '../lib/api';
import { askConfirm } from '../lib/dialogs';

const people = (n: number) => `${n} ${n === 1 ? 'person' : 'people'}`;

export function EmailConsentPage() {
  const queryClient = useQueryClient();
  const summary = useQuery({
    queryKey: ['email-consent'],
    queryFn: async () => unwrap(await api.GET('/api/admin/people/email-consent')),
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [result, setResult] = useState<Schemas['EmailConsentSendResult'] | null>(null);

  const send = async () => {
    const count = summary.data?.canBeAsked ?? 0;
    const ok = await askConfirm(
      `Email ${people(count)} once, asking whether they'd like church emails? Each person can only ever be asked once, so this can't be repeated for them.`,
      { title: 'Send consent requests', confirmLabel: `Email ${people(count)}` },
    );
    if (!ok) return;
    setBusy(true);
    setError(null);
    try {
      setResult(unwrap(await api.POST('/api/admin/people/email-consent/send')));
      await queryClient.invalidateQueries({ queryKey: ['email-consent'] });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const s = summary.data;
  return (
    <>
      <PageHeader
        title="Ask for email consent"
        subtitle="Church emails only reach people who've agreed to them. The law lets us ask each person once."
      />
      <Card>
        <ErrorNote error={summary.error} />
        {summary.isPending ? (
          <Loading />
        ) : s && (
          <div className="stack">
            <ul>
              <li><strong>{people(s.canBeAsked)}</strong> can be asked: they have an email address and haven't been asked or answered yet.</li>
              <li>{people(s.alreadyAgreed)} already get church emails.</li>
              <li>{people(s.saidNo)} said no, so won't be asked.</li>
              <li>{people(s.alreadyAsked)} were asked and haven't answered. They won't be asked again.</li>
              <li>{people(s.noEmail)} have no email address.</li>
            </ul>
            <p className="muted small">
              Each person gets one email with a link to say yes or no. Their answer is saved on their profile, and anyone who says yes
              starts receiving announcements by email. Ignoring the email counts as no.
            </p>
            <div className="form-actions">
              <ErrorNote error={error} />
              <Button variant="primary" disabled={s.canBeAsked === 0} busy={busy} onClick={send}>
                {s.canBeAsked > 0 ? `Email ${people(s.canBeAsked)}` : 'No one to ask'}
              </Button>
            </div>
            {result && (
              <p>
                Sent to {people(result.sent)}.
                {result.failed > 0 && ` ${people(result.failed)} couldn't be reached; they're marked as asked, so check the email setup before anything else.`}
              </p>
            )}
          </div>
        )}
      </Card>
    </>
  );
}
