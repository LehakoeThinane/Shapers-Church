import { useQueryClient } from '@tanstack/react-query';
import { NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import type { PalettePreference } from '@shapers/tokens';
import { api } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { usePalette } from '../lib/palette-context';
import { scopeLabel, useScope } from '../lib/scope-context';
import { Button, Loading, Select } from './ui';

const nav = [
  { to: '/people', label: 'People', permission: Permissions.peopleView },
  { to: '/duplicates', label: 'Duplicates', permission: Permissions.peopleMerge },
  { to: '/sermons', label: 'Sermons', permission: Permissions.mediaEdit },
  { to: '/church', label: 'Campuses & ministries', permission: null },
  { to: '/access', label: 'Roles & access', permission: Permissions.usersView },
  { to: '/audit', label: 'Audit log', permission: Permissions.auditView },
  { to: '/security', label: 'Security', permission: null },
] as const;

export function Layout() {
  const { data: access, isPending, isError } = useAccess();
  const { preference, setPreference } = usePalette();
  const scope = useScope();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  if (isPending) return <Loading />;
  if (isError || !access) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname)}`} replace />;

  const signOut = async () => {
    await api.POST('/api/auth/staff/logout');
    queryClient.clear();
    navigate('/login');
  };

  return (
    <div className="app">
      <aside className="sidebar glass">
        <div className="brand">
          <span className="brand-mark">Shapers</span>
          <span className="muted small">Church admin</span>
        </div>
        <nav>
          {nav
            .filter((item) => item.permission === null || can(access, item.permission))
            .map((item) => (
              <NavLink key={item.to} to={item.to} className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
                {item.label}
              </NavLink>
            ))}
        </nav>
        <div className="sidebar-footer">
          <label className="field">
            <span className="field-label">Palette</span>
            <Select value={preference} onChange={(e) => setPreference(e.target.value as PalettePreference)}>
              <option value="auto">Automatic</option>
              <option value="midnight">Midnight</option>
              <option value="rose">Rose</option>
            </Select>
          </label>
          <p className="small muted">{access.displayName}</p>
          <Button variant="ghost" onClick={signOut}>
            Sign out
          </Button>
        </div>
      </aside>

      <div className="main">
        <header className="topbar">
          <label className="row">
            <span className="small muted">Working in</span>
            <Select value={scope.current ?? ''} onChange={(e) => scope.setCurrent(e.target.value || null)}>
              <option value="">Everything I can see</option>
              {scope.options.map((o) => (
                <option key={o.path} value={o.path}>
                  {scopeLabel(o)}
                </option>
              ))}
            </Select>
          </label>
        </header>

        {access.mfaRequiredForSensitive && !access.hasMfa && (
          <p className="note note-accent">
            {access.twoFactorEnabled
              ? 'Sign in again with your authenticator code to open screens with personal information.'
              : 'Set up two-step verification to open screens with personal information. '}
            {!access.twoFactorEnabled && <NavLink to="/security">Set it up</NavLink>}
          </p>
        )}

        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
