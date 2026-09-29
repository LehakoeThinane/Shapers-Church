import { useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { Button, ErrorNote, Field, TextInput } from '../components/ui';
import { api, unwrap } from '../lib/api';

export function LoginPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [useRecovery, setUseRecovery] = useState(false);
  const [needsCode, setNeedsCode] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = unwrap(
        await api.POST('/api/auth/staff/login', {
          body: {
            email,
            password,
            twoFactorCode: needsCode && !useRecovery ? code : null,
            recoveryCode: needsCode && useRecovery ? code : null,
          },
        }),
      );
      if (result.status === 'TwoFactorRequired') {
        setNeedsCode(true);
        return;
      }

      await queryClient.invalidateQueries();
      const next = params.get('next');
      navigate(next?.startsWith('/') ? next : '/people', { replace: true });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="centered">
      <form className="card glass auth-card" onSubmit={submit}>
        <h1>Shapers admin</h1>
        <p className="muted">Sign in to manage the church database.</p>

        <Field label="Email">
          <TextInput type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} disabled={needsCode} />
        </Field>
        <Field label="Password">
          <TextInput type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} disabled={needsCode} />
        </Field>

        {needsCode && (
          <>
            <Field label={useRecovery ? 'Recovery code' : 'Code from your authenticator app'}>
              <TextInput
                autoFocus
                inputMode={useRecovery ? 'text' : 'numeric'}
                autoComplete="one-time-code"
                required
                value={code}
                onChange={(e) => setCode(e.target.value)}
              />
            </Field>
            <button type="button" className="link-button" onClick={() => setUseRecovery(!useRecovery)}>
              {useRecovery ? 'Use my authenticator app instead' : 'Use a recovery code instead'}
            </button>
          </>
        )}

        <ErrorNote error={error} />
        <Button variant="primary" type="submit" busy={busy}>
          {needsCode ? 'Verify and sign in' : 'Sign in'}
        </Button>
      </form>
    </div>
  );
}

export function SetPasswordPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const userId = params.get('userId') ?? '';
  const token = params.get('token') ?? '';

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (password !== confirm) {
      setError(new Error('The two passwords are different.'));
      return;
    }

    setBusy(true);
    setError(null);
    try {
      unwrap(await api.POST('/api/auth/staff/set-password', { body: { userId, token, password } }));
      navigate('/login', { replace: true });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="centered">
      <form className="card glass auth-card" onSubmit={submit}>
        <h1>Choose a password</h1>
        <p className="muted">Use at least 12 characters. A short sentence is easy to remember and hard to guess.</p>
        <Field label="New password">
          <TextInput type="password" autoComplete="new-password" minLength={12} required value={password} onChange={(e) => setPassword(e.target.value)} />
        </Field>
        <Field label="Confirm password">
          <TextInput type="password" autoComplete="new-password" minLength={12} required value={confirm} onChange={(e) => setConfirm(e.target.value)} />
        </Field>
        <ErrorNote error={error} />
        <Button variant="primary" type="submit" busy={busy} disabled={!userId || !token}>
          Save password
        </Button>
      </form>
    </div>
  );
}
