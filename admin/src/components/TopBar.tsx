import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { PalettePreference } from '@shapers/tokens';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate } from 'react-router';
import { api, formatDateTime, unwrap, type Schemas } from '../lib/api';
import type { Access } from '../lib/access';
import { navIcons } from '../lib/navigation';
import { usePalette } from '../lib/palette-context';

type Topic = Schemas['Topic'];

/** Where a notification leads in the admin portal (the same messages lead elsewhere in the app). */
const topicPage: Record<Topic, string> = {
  Live: '/chat',
  Sermons: '/sermons',
  Events: '/events',
  Prayer: '/prayer',
  Announcements: '/announcements',
  Serving: '/services/matrix',
};

/** A small menu that opens under its button and closes on outside click or Escape. */
function Popover({ button, label, children, open, setOpen }: { button: ReactNode; label: string; children: ReactNode; open: boolean; setOpen: (open: boolean) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', onClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [open, setOpen]);
  return (
    <div className="popover-anchor" ref={ref}>
      <button type="button" className="icon-button" aria-haspopup="true" aria-expanded={open} aria-label={label} onClick={() => setOpen(!open)}>
        {button}
      </button>
      {open && (
        <div className="popover glass" role="dialog" aria-label={label}>
          {children}
        </div>
      )}
    </div>
  );
}

/**
 * Everything waiting for this person: account reminders (like setting up two-step verification) and their
 * notifications (urgent cell follow-ups, someone who can't serve, a reported chat message, ...).
 */
export function NotificationsBell({ access }: { access: Access }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const inbox = useQuery({
    queryKey: ['me', 'notifications'],
    queryFn: async () => unwrap(await api.GET('/api/me/notifications', { params: { query: { page: 1 } } })),
    refetchInterval: 60_000,
    retry: false,
  });
  const read = useMutation({
    mutationFn: async (id: string | null) =>
      id
        ? unwrap(await api.POST('/api/me/notifications/{id}/read', { params: { path: { id } } }))
        : unwrap(await api.POST('/api/me/notifications/read-all')),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me', 'notifications'] }),
  });

  const reminders: { key: string; title: string; body: string; to: string; action: string }[] = [];
  if (access.mfaRequiredForSensitive && !access.twoFactorEnabled) {
    reminders.push({
      key: 'mfa-setup',
      title: 'Protect your account',
      body: 'Set up two-step verification to open screens with people’s personal details, like People, prayer and cell reports.',
      to: '/security',
      action: 'Set it up',
    });
  } else if (access.mfaRequiredForSensitive && !access.hasMfa) {
    reminders.push({
      key: 'mfa-signin',
      title: 'Sign in with your code',
      body: 'You signed in without your authenticator code, so screens with personal details are closed. Sign out and in again to open them.',
      to: '/security',
      action: 'My security',
    });
  }

  const items = inbox.data?.items ?? [];
  const count = reminders.length + (inbox.data?.unread ?? 0);

  return (
    <Popover
      open={open}
      setOpen={setOpen}
      label={count > 0 ? `Notifications, ${count} new` : 'Notifications'}
      button={
        <>
          {navIcons.bell}
          {count > 0 && <span className="bell-count">{count > 9 ? '9+' : count}</span>}
        </>
      }
    >
      <div className="popover-head">
        <strong>Notifications</strong>
        {(inbox.data?.unread ?? 0) > 0 && (
          <button type="button" className="link-button small" onClick={() => read.mutate(null)}>
            Mark all as read
          </button>
        )}
      </div>
      <ul className="notice-list">
        {reminders.map((r) => (
          <li key={r.key} className="notice notice-reminder">
            <strong>{r.title}</strong>
            <span className="small">{r.body}</span>
            <Link to={r.to} className="small" onClick={() => setOpen(false)}>
              {r.action}
            </Link>
          </li>
        ))}
        {items.map((n) => (
          <li key={n.id} className={`notice${n.read ? '' : ' notice-unread'}`}>
            <button
              type="button"
              className="notice-button"
              onClick={() => {
                if (!n.read) read.mutate(n.id);
                setOpen(false);
                navigate(topicPage[n.topic]);
              }}
            >
              <strong>{n.title}</strong>
              <span className="small">{n.body}</span>
              <span className="small muted">{formatDateTime(n.createdAt)}</span>
            </button>
          </li>
        ))}
        {reminders.length === 0 && items.length === 0 && <li className="notice small muted">You’re all caught up.</li>}
      </ul>
    </Popover>
  );
}

const palettes: { value: PalettePreference; label: string }[] = [
  { value: 'auto', label: 'Automatic' },
  { value: 'midnight', label: 'Midnight' },
  { value: 'rose', label: 'Rose' },
];

/** The person's own things: who's signed in, security, colours, signing out. */
export function AccountMenu({ access, onSignOut }: { access: Access; onSignOut: () => void }) {
  const [open, setOpen] = useState(false);
  const { preference, setPreference } = usePalette();
  const initials = access.displayName
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase())
    .join('');

  return (
    <Popover open={open} setOpen={setOpen} label="Your account" button={<span className="avatar">{initials || '?'}</span>}>
      <div className="popover-head">
        <span className="stack-tight">
          <strong>{access.displayName}</strong>
          <span className="small muted">{access.twoFactorEnabled ? 'Two-step verification is on' : 'Two-step verification is off'}</span>
        </span>
      </div>
      <div className="stack">
        <Link to="/security" className="menu-link" onClick={() => setOpen(false)}>
          My security
        </Link>
        <div className="stack-tight">
          <span className="small muted">Colours</span>
          <div className="segmented" role="radiogroup" aria-label="Colours">
            {palettes.map((p) => (
              <button
                key={p.value}
                type="button"
                role="radio"
                aria-checked={preference === p.value}
                className={`segment${preference === p.value ? ' active' : ''}`}
                onClick={() => setPreference(p.value)}
              >
                {p.label}
              </button>
            ))}
          </div>
        </div>
        <button type="button" className="menu-link" onClick={onSignOut}>
          Sign out
        </button>
      </div>
    </Popover>
  );
}
