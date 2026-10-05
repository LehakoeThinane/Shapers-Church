import { useQueryClient } from '@tanstack/react-query';
import { NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import type { PalettePreference } from '@shapers/tokens';
import { api } from '../lib/api';
import { useAccess } from '../lib/access';
import { usePalette } from '../lib/palette-context';
import { allowed, productFor, productGroups } from '../lib/products';
import { scopeLabel, useScope } from '../lib/scope-context';
import { Button, Loading, Select } from './ui';

export function Layout() {
  const { data: access, isPending, isError } = useAccess();
  const { preference, setPreference } = usePalette();
  const scope = useScope();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  if (isPending) return <Loading />;
  if (isError || !access) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname)}`} replace />;

  const current = productFor(location.pathname);

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
        <nav aria-label="Products">
          <NavLink to="/" end className={({ isActive }) => `nav-link nav-home${isActive ? ' active' : ''}`}>
            Home
          </NavLink>
          {productGroups.map((group) => {
            const products = group.products.filter((p) => p.status === 'live' && allowed(access, p.permission));
            if (products.length === 0) return null;
            return (
              <div key={group.title} className="nav-group">
                <span className="nav-group-title">{group.title}</span>
                {products.map((p) => {
                  const isCurrent = current?.key === p.key;
                  const links = (p.links ?? []).filter((l) => allowed(access, l.permission === undefined ? p.permission : l.permission));
                  return (
                    <div key={p.key}>
                      <NavLink to={p.to!} className={`nav-link nav-product${isCurrent ? ' active' : ''}`}>
                        <span className="nav-icon">{p.icon}</span>
                        {p.name}
                      </NavLink>
                      {isCurrent && links.length > 1 && (
                        <div className="nav-sub">
                          {links.map((l) => (
                            <NavLink key={l.to} to={l.to} end={l.to === p.to} className={({ isActive }) => `nav-link nav-sub-link${isActive ? ' active' : ''}`}>
                              {l.label}
                            </NavLink>
                          ))}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            );
          })}
        </nav>
        <div className="sidebar-footer">
          <NavLink to="/security" className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
            My security
          </NavLink>
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
