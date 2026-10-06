import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import { api } from '../lib/api';
import { useAccess, type Access } from '../lib/access';
import { isLeader, useMyCells } from '../lib/cells';
import { currentTab, navIcons, navItemFor, navSections, settingsItems, visibleTabs, type NavItem } from '../lib/navigation';
import { allowed, cellsIcon } from '../lib/products';
import { scopeLabel, useScope } from '../lib/scope-context';
import { AccountMenu, NotificationsBell } from './TopBar';
import { Loading, Select } from './ui';

const COLLAPSED_KEY = 'shapers.menu.collapsed';

function readCollapsed() {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === '1';
  } catch {
    return false;
  }
}

export function Layout() {
  const { data: access, isPending, isError } = useAccess();
  const scope = useScope();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const myCells = useMyCells();
  const led = myCells.data?.filter((c) => isLeader(c.myRole)) ?? [];
  const [collapsed, setCollapsed] = useState(readCollapsed);
  // The phone drawer is open on the page it was opened on; choosing a page closes it.
  const [drawerOn, setDrawerOn] = useState<string | null>(null);
  const drawerOpen = drawerOn === location.pathname;
  const setDrawerOpen = (open: boolean) => setDrawerOn(open ? location.pathname : null);

  if (isPending) return <Loading />;
  if (isError || !access) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname)}`} replace />;

  const toggleCollapsed = () => {
    const next = !collapsed;
    setCollapsed(next);
    try {
      localStorage.setItem(COLLAPSED_KEY, next ? '1' : '0');
    } catch {
      // Private windows may refuse storage; the menu just won't remember.
    }
  };

  const signOut = async () => {
    await api.POST('/api/auth/staff/logout');
    queryClient.clear();
    navigate('/login');
  };

  const current = navItemFor(location.pathname);
  const tabs = visibleTabs(current, access);
  const activeTab = currentTab(location.pathname, tabs);
  const settings = settingsItems.filter((i) => allowed(access, i.permission));

  return (
    <div className={`shell${collapsed ? ' shell-collapsed' : ''}${drawerOpen ? ' shell-drawer-open' : ''}`}>
      <aside className="rail glass" aria-label="Main menu">
        <div className="rail-brand">
          <span className="rail-logo" aria-hidden="true">
            S
          </span>
          <span className="rail-name">
            <span className="brand-mark">Shapers</span>
            <span className="small muted">Church admin</span>
          </span>
          <button type="button" className="rail-collapse" onClick={toggleCollapsed} aria-label={collapsed ? 'Expand the menu' : 'Minimise the menu'} aria-expanded={!collapsed} title={collapsed ? 'Expand' : 'Minimise'}>
            {collapsed ? navIcons.expand : navIcons.collapse}
          </button>
        </div>

        <nav className="rail-nav">
          <RailLink to="/" end icon={navIcons.home} label="Home" />
          {led.length > 0 && (
            <RailSection title="My cell">
              {led.map((c) => (
                <RailLink key={c.id} to={`/my-cells/${c.id}`} icon={cellsIcon} label={c.name} />
              ))}
            </RailSection>
          )}
          {navSections.map((section) => {
            const items = section.items.filter((i) => allowed(access, i.permission));
            if (items.length === 0) return null;
            return (
              <RailSection key={section.title} title={section.title}>
                {items.map((i) => (
                  <RailItem key={i.key} item={i} active={current?.key === i.key} access={access} />
                ))}
              </RailSection>
            );
          })}
        </nav>

        {settings.length > 0 && (
          <div className="rail-foot">
            <details className="rail-settings" open={settings.some((i) => i.key === current?.key) || undefined}>
              <summary className="rail-link" title="Settings">
                <span className="rail-icon">{navIcons.settings}</span>
                <span className="rail-label">Settings</span>
              </summary>
              {settings.map((i) => (
                <RailItem key={i.key} item={i} active={current?.key === i.key} access={access} />
              ))}
            </details>
          </div>
        )}
      </aside>
      <button type="button" className="drawer-scrim" aria-label="Close the menu" onClick={() => setDrawerOpen(false)} />

      <div className="main">
        <header className="topbar">
          <button type="button" className="icon-button menu-button" aria-label="Open the menu" onClick={() => setDrawerOpen(true)}>
            {navIcons.menu}
          </button>
          <span className="topbar-title">{current?.label ?? (location.pathname === '/' ? 'Home' : '')}</span>
          <span className="topbar-spacer" />
          {scope.options.length > 1 && (
            <Select className="input scope-select" aria-label="Working in" value={scope.current ?? ''} onChange={(e) => scope.setCurrent(e.target.value || null)}>
              <option value="">All campuses and ministries</option>
              {scope.options.map((o) => (
                <option key={o.path} value={o.path}>
                  {scopeLabel(o)}
                </option>
              ))}
            </Select>
          )}
          <NotificationsBell access={access} />
          <AccountMenu access={access} onSignOut={signOut} />
        </header>

        {tabs.length > 1 && (
          <nav className="page-tabs" aria-label={`${current!.label} pages`}>
            {tabs.map((t) => (
              <NavLink key={t.to} to={t.to} className={() => `page-tab${activeTab === t.to ? ' active' : ''}`} aria-current={activeTab === t.to ? 'page' : undefined}>
                {t.label}
              </NavLink>
            ))}
          </nav>
        )}

        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

function RailSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rail-section">
      <span className="rail-section-title">{title}</span>
      {children}
    </div>
  );
}

function RailLink({ to, icon, label, end, active }: { to: string; icon: React.ReactNode; label: string; end?: boolean; active?: boolean }) {
  return (
    <NavLink to={to} end={end} title={label} className={({ isActive }) => `rail-link${(active ?? isActive) ? ' active' : ''}`}>
      <span className="rail-icon">{icon}</span>
      <span className="rail-label">{label}</span>
    </NavLink>
  );
}

/** A menu item goes to its first page the person can open. */
function RailItem({ item, active, access }: { item: NavItem; active: boolean; access: Access }) {
  const first = visibleTabs(item, access)[0]?.to ?? item.to;
  return <RailLink to={first} icon={item.icon} label={item.label} active={active} />;
}
