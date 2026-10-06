import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Card } from '../components/ui';
import { api, formatDateTime, unwrap } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { isLeader, useMyCells } from '../lib/cells';
import { navSections } from '../lib/navigation';
import { allowed, productGroups } from '../lib/products';

/** The first page after sign-in: what's waiting for you, what's coming up, and every product. */
export function HomePage() {
  const { data: access } = useAccess();
  const name = access?.displayName.split(' ')[0] ?? '';
  const led = useMyCells().data?.filter((c) => isLeader(c.myRole)) ?? [];
  const anyTasks = [
    Permissions.prayerModerate,
    Permissions.peopleView,
    Permissions.announcementsApprove,
    Permissions.privacyRequests,
    Permissions.eventsEdit,
    Permissions.eventsCheckIn,
    Permissions.cellReportsView,
  ].some((p) => can(access, p)) || led.length > 0;

  return (
    <>
      <header className="home-hero">
        <p className="small muted">{greeting()}</p>
        <h1>
          {name ? `${name}, ` : ''}here&apos;s what&apos;s <span className="serif-accent">waiting for you.</span>
        </h1>
      </header>

      <section aria-labelledby="tasks-heading" className="stack">
        <h2 id="tasks-heading">Waiting for you</h2>
        {!anyTasks && <NothingWaiting />}
        <div className="task-grid">
          {led.map((c) => (
            <LeaderTask key={c.id} cellId={c.id} name={c.name} />
          ))}
          {can(access, Permissions.cellReportsView) && <CellsTasks />}
          {can(access, Permissions.prayerModerate) && <PrayerTask />}
          {can(access, Permissions.peopleView) && <ConnectTask />}
          {can(access, Permissions.announcementsApprove) && <ApprovalTask />}
          {can(access, Permissions.privacyRequests) && <PrivacyTask />}
          {(can(access, Permissions.eventsEdit) || can(access, Permissions.eventsCheckIn)) && <EventsTask />}
        </div>
      </section>

      <section aria-labelledby="go-heading" className="stack">
        <h2 id="go-heading">Jump to</h2>
        <div className="product-groups">
          {navSections.map((section) => {
            const items = section.items.filter((i) => allowed(access, i.permission));
            if (items.length === 0) return null;
            return (
              <div key={section.title} className="product-group">
                <h3 className="product-group-title">{section.title}</h3>
                {items.map((i) => (
                  <Link key={i.key} to={i.to} className="product">
                    <span className="product-icon">{i.icon}</span>
                    <span className="stack-tight">
                      <span className="product-name">{i.label}</span>
                      <span className="small muted">{describe(i.key)}</span>
                    </span>
                  </Link>
                ))}
              </div>
            );
          })}
        </div>
        <p className="small muted">Coming next: giving, kids check-in, the church calendar and a staff app.</p>
      </section>
    </>
  );
}

function greeting() {
  const hour = Number(new Intl.DateTimeFormat('en-ZA', { hour: 'numeric', hour12: false, timeZone: 'Africa/Johannesburg' }).format(new Date()));
  return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
}

/** One "waiting for you" card: a count, what it means, and where to go. */
function Task({ to, count, label, detail, loading, failed }: { to: string; count: number | undefined; label: string; detail?: ReactNode; loading: boolean; failed?: boolean }) {
  const done = !loading && !failed && count === 0;
  return (
    <Link to={to} className={`task glass${done ? ' task-done' : ''}`}>
      <span className="task-count">{loading ? '…' : failed ? '!' : done ? '✓' : count}</span>
      <span className="stack-tight">
        <strong>{label}</strong>
        <span className="small muted">{loading ? 'Checking…' : failed ? "Couldn't check. Open to see the list." : done ? 'All caught up' : detail}</span>
      </span>
    </Link>
  );
}

function PrayerTask() {
  const q = useQuery({ queryKey: ['prayer', 'review'], queryFn: async () => unwrap(await api.GET('/api/admin/prayer/review')) });
  return <Task to="/prayer" loading={q.isPending} failed={q.isError} count={q.data?.length} label="Prayer requests to review" detail="Before they go on the prayer wall" />;
}

function ConnectTask() {
  const q = useQuery({
    queryKey: ['connect-cards', 'New'],
    queryFn: async () => unwrap(await api.GET('/api/admin/connect-cards', { params: { query: { status: 'New' } } })),
  });
  return <Task to="/connect" loading={q.isPending} failed={q.isError} count={q.data?.length} label="New connect cards" detail="People waiting to hear from the church" />;
}

function ApprovalTask() {
  const q = useQuery({ queryKey: ['announcements'], queryFn: async () => unwrap(await api.GET('/api/admin/announcements')) });
  const waiting = q.data?.filter((a) => a.status === 'AwaitingApproval' && !a.createdByMe);
  return <Task to="/announcements" loading={q.isPending} failed={q.isError} count={waiting?.length} label="Announcements to approve" detail="Written by someone else, waiting for your OK" />;
}

function PrivacyTask() {
  const q = useQuery({
    queryKey: ['privacy-requests', 'Open'],
    queryFn: async () => unwrap(await api.GET('/api/admin/privacy/requests', { params: { query: { status: 'Open' } } })),
  });
  const overdue = q.data?.filter((r) => r.overdue).length ?? 0;
  const next = q.data?.map((r) => r.dueAt).sort()[0];
  return (
    <Task
      to="/privacy"
      loading={q.isPending} failed={q.isError}
      count={q.data?.length}
      label="Privacy requests open"
      detail={overdue > 0 ? <span className="danger-text">{overdue} overdue</span> : next ? `Next due ${formatDateTime(next)}` : undefined}
    />
  );
}

function CellsTasks() {
  const q = useQuery({ queryKey: ['cells'], queryFn: async () => unwrap(await api.GET('/api/admin/cells')) });
  const missing = q.data?.filter((c) => !c.reportedThisWeek);
  const urgent = q.data?.reduce((sum, c) => sum + c.openUrgentFollowUps, 0);
  return (
    <>
      <Task
        to="/cells"
        loading={q.isPending}
        failed={q.isError}
        count={missing?.length}
        label="Cell reports missing"
        detail={missing && missing.length > 0 ? `${missing.slice(0, 3).map((c) => c.name).join(', ')}${missing.length > 3 ? '…' : ''}` : undefined}
      />
      <Task to="/cells/reports?urgent=1" loading={q.isPending} failed={q.isError} count={urgent} label="Urgent cell follow-ups" detail="Flagged by cell leaders for a pastor" />
    </>
  );
}

/** Leaders: a nudge to write up this week's meeting. */
function LeaderTask({ cellId, name }: { cellId: string; name: string }) {
  const q = useQuery({
    queryKey: ['my-cell-reports', cellId, 'this-week'],
    queryFn: async () => {
      const reports = unwrap(await api.GET('/api/cells/{cellId}/reports', { params: { path: { cellId } } }));
      const weekAgo = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
      return reports.filter((r) => r.meetingDate >= weekAgo);
    },
  });
  const drafts = q.data?.filter((r) => r.status === 'Draft') ?? [];
  const submitted = (q.data?.length ?? 0) - drafts.length;
  return (
    <Task
      to={drafts[0] ? `/my-cells/${cellId}/reports/${drafts[0].id}` : submitted > 0 ? `/my-cells/${cellId}` : `/my-cells/${cellId}/reports/new`}
      loading={q.isPending}
      failed={q.isError}
      count={submitted > 0 && drafts.length === 0 ? 0 : 1}
      label={`${name}: this week's report`}
      detail={drafts.length > 0 ? 'A draft is waiting to be submitted' : 'Record who came and how it went'}
    />
  );
}

function EventsTask() {
  // "This week" is worked out when the list is fetched, not on every render.
  const q = useQuery({
    queryKey: ['events', 'this-week'],
    queryFn: async () => {
      const events = unwrap(await api.GET('/api/admin/events'));
      const now = Date.now();
      const week = now + 7 * 24 * 60 * 60 * 1000;
      return events
        .filter((e) => e.status === 'Published' && new Date(e.event.startsAt).getTime() >= now && new Date(e.event.startsAt).getTime() <= week)
        .sort((a, b) => a.event.startsAt.localeCompare(b.event.startsAt));
    },
  });
  const soon = q.data;
  const first = soon?.[0];
  return (
    <Task
      to={first ? `/events/${first.event.id}/attendees` : '/events'}
      loading={q.isPending} failed={q.isError}
      count={soon?.length}
      label="Events this week"
      detail={first ? `${first.event.title}, ${formatDateTime(first.event.startsAt)} · ${first.confirmed} booked` : undefined}
    />
  );
}

/** Shown when none of the tasks apply, so a new account doesn't land on an empty section. */
function NothingWaiting() {
  return (
    <Card>
      <p className="muted">Nothing needs you right now. Choose a product below to get started.</p>
    </Card>
  );
}

/** The one-line description from the product catalogue, matched by key. */
function describe(key: string) {
  const own: Record<string, string> = {
    sermons: 'Sermons, series, speakers and the Sunday livestream',
    website: 'Website pages, news, blog and translations',
  };
  if (own[key]) return own[key];
  const aliases: Record<string, string> = { events: 'registrations' };
  return productGroups.flatMap((g) => g.products).find((p) => p.key === (aliases[key] ?? key))?.description ?? '';
}
