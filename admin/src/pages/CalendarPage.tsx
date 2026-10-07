import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { Badge, Card, Empty, ErrorNote, Loading, PageHeader, ToggleChip } from '../components/ui';
import { api, apiBaseUrl, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess, type Access } from '../lib/access';

type Entry = Schemas['CalendarEntry'];
type Kind = Entry['kind'];

const ZONE = 'Africa/Johannesburg';
const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const SHOWN_PER_DAY = 3;

const kindLabel: Record<Kind, string> = { Event: 'Event', Service: 'Service', Livestream: 'Livestream', Cell: 'Home cell', Serving: 'You’re serving' };

/** "2026-10-07" for an instant, as the church's calendar date. */
const dayKey = (iso: string) => new Intl.DateTimeFormat('en-CA', { timeZone: ZONE, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(iso));
const timeOf = (iso: string) => new Intl.DateTimeFormat('en-ZA', { timeZone: ZONE, hour: '2-digit', minute: '2-digit' }).format(new Date(iso));

function addDays(key: string, days: number) {
  const d = new Date(`${key}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** Monday-to-Sunday weeks covering the month. */
function monthDays(year: number, month: number) {
  const first = new Date(Date.UTC(year, month, 1));
  const last = new Date(Date.UTC(year, month + 1, 0));
  const start = addDays(first.toISOString().slice(0, 10), -((first.getUTCDay() + 6) % 7));
  const end = addDays(last.toISOString().slice(0, 10), 6 - ((last.getUTCDay() + 6) % 7));
  const days: string[] = [];
  for (let d = start; d <= end; d = addDays(d, 1)) days.push(d);
  return days;
}

/** Where an entry opens in the portal, if this person can open it there. */
function linkFor(e: Entry, access: Access | undefined): string | null {
  switch (e.kind) {
    case 'Event':
      return can(access, Permissions.eventsEdit) ? `/events/${e.refId}` : null;
    case 'Service':
      return `/services/plans/${e.refId}`;
    case 'Livestream':
      return can(access, Permissions.livestreamManage) ? `/livestreams/${e.refId}` : null;
    case 'Cell':
      return can(access, Permissions.cellsManage) || can(access, Permissions.cellReportsView) ? `/cells/${e.refId}` : null;
    default:
      return null;
  }
}

/** Everything on in a month: events, livestreams, services and cells, each as this person may see it. */
export function CalendarPage() {
  const { data: access } = useAccess();
  const [today] = useState(() => dayKey(new Date().toISOString()));
  const [month, setMonth] = useState(() => ({ year: Number(today.slice(0, 4)), month: Number(today.slice(5, 7)) - 1 }));
  const [shown, setShown] = useState<Record<'Event' | 'Service' | 'Livestream' | 'Cell', boolean>>({ Event: true, Service: true, Livestream: true, Cell: false });
  const [openDay, setOpenDay] = useState<string | null>(null);

  const days = monthDays(month.year, month.month);
  const from = `${days[0]}T00:00:00+02:00`;
  const to = `${addDays(days[days.length - 1]!, 1)}T00:00:00+02:00`;
  const calendar = useQuery({
    queryKey: ['calendar', from, to, shown.Cell],
    queryFn: async () => unwrap(await api.GET('/api/calendar', { params: { query: { from, to, allCells: shown.Cell } } })),
  });

  const canServices = can(access, Permissions.servicesPlans) || can(access, Permissions.servicesSchedule);
  const canCells = can(access, Permissions.cellsManage);
  const visible = (calendar.data ?? []).filter((e) => e.mine || e.kind === 'Serving' || (e.kind in shown && shown[e.kind as keyof typeof shown]));
  const byDay = new Map<string, Entry[]>();
  for (const e of visible) byDay.set(dayKey(e.startsAt), [...(byDay.get(dayKey(e.startsAt)) ?? []), e]);

  const title = new Intl.DateTimeFormat('en-ZA', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(month.year, month.month, 1)));
  const step = (by: number) => {
    const d = new Date(Date.UTC(month.year, month.month + by, 1));
    setMonth({ year: d.getUTCFullYear(), month: d.getUTCMonth() });
    setOpenDay(null);
  };
  const inMonth = (key: string) => Number(key.slice(5, 7)) - 1 === month.month;
  const monthEntries = [...byDay.entries()].filter(([key]) => inMonth(key)).sort(([a], [b]) => a.localeCompare(b));

  return (
    <>
      <PageHeader
        title="Calendar"
        subtitle="What’s on across the church. Members see the public events and livestreams, plus their own cell and serving dates."
        actions={
          <a className="btn btn-secondary" href={`${apiBaseUrl}/calendar.ics`} target="_blank" rel="noopener noreferrer">
            Public calendar feed
          </a>
        }
      />

      <div className="cal-bar">
        <div className="row">
          <button type="button" className="icon-button" aria-label="Previous month" onClick={() => step(-1)}>
            ‹
          </button>
          <h2 className="cal-title" aria-live="polite">
            {title}
          </h2>
          <button type="button" className="icon-button" aria-label="Next month" onClick={() => step(1)}>
            ›
          </button>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => {
              setMonth({ year: Number(today.slice(0, 4)), month: Number(today.slice(5, 7)) - 1 });
              setOpenDay(null);
            }}
          >
            Today
          </button>
        </div>
        <div className="row" role="group" aria-label="Show">
          <ToggleChip on={shown.Event} onChange={(on) => setShown({ ...shown, Event: on })} dot="kind-event">
            Events
          </ToggleChip>
          <ToggleChip on={shown.Livestream} onChange={(on) => setShown({ ...shown, Livestream: on })} dot="kind-livestream">
            Livestreams
          </ToggleChip>
          {canServices && (
            <ToggleChip on={shown.Service} onChange={(on) => setShown({ ...shown, Service: on })} dot="kind-service">
              Services
            </ToggleChip>
          )}
          {canCells && (
            <ToggleChip on={shown.Cell} onChange={(on) => setShown({ ...shown, Cell: on })} dot="kind-cell">
              All home cells
            </ToggleChip>
          )}
        </div>
      </div>

      {calendar.isPending && <Loading />}
      <ErrorNote error={calendar.error} />

      {calendar.data && (
        <>
          <Card className="cal-card">
            <div className="cal-grid">
              {WEEKDAYS.map((d) => (
                <div key={d} className="cal-weekday" aria-hidden="true">
                  {d}
                </div>
              ))}
              {days.map((key) => {
                const entries = byDay.get(key) ?? [];
                const open = openDay === key;
                const list = open ? entries : entries.slice(0, SHOWN_PER_DAY);
                return (
                  <section
                    key={key}
                    className={`cal-day${inMonth(key) ? '' : ' cal-day-outside'}${key === today ? ' cal-day-today' : ''}`}
                    aria-label={new Intl.DateTimeFormat('en-ZA', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).format(new Date(`${key}T00:00:00Z`))}
                  >
                    <span className="cal-date">{Number(key.slice(8))}</span>
                    {list.map((e) => (
                      <EntryLine key={`${e.kind}-${e.refId}-${e.startsAt}`} entry={e} access={access} compact />
                    ))}
                    {entries.length > SHOWN_PER_DAY && (
                      <button type="button" className="link-button small" onClick={() => setOpenDay(open ? null : key)}>
                        {open ? 'Show less' : `+${entries.length - SHOWN_PER_DAY} more`}
                      </button>
                    )}
                  </section>
                );
              })}
            </div>

            <div className="cal-list">
              {monthEntries.length === 0 && <Empty>Nothing on the calendar in {title}.</Empty>}
              {monthEntries.map(([key, entries]) => (
                <section key={key} className="stack">
                  <h3 className={`cal-list-day${key === today ? ' cal-list-today' : ''}`}>
                    {key === today ? 'Today, ' : ''}
                    {new Intl.DateTimeFormat('en-ZA', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).format(new Date(`${key}T00:00:00Z`))}
                  </h3>
                  {entries.map((e) => (
                    <EntryLine key={`${e.kind}-${e.refId}-${e.startsAt}`} entry={e} access={access} />
                  ))}
                </section>
              ))}
            </div>
          </Card>
          {visible.length === 0 && <p className="small muted">Nothing on the calendar in {title} for what’s switched on above.</p>}
        </>
      )}
    </>
  );
}

function EntryLine({ entry: e, access, compact = false }: { entry: Entry; access: Access | undefined; compact?: boolean }) {
  const to = linkFor(e, access);
  const body = (
    <>
      <span className="cal-time">{timeOf(e.startsAt)}</span> <span className="cal-entry-title">{e.title}</span>
      {!compact && (
        <span className="small muted">
          {' '}
          · {kindLabel[e.kind]}
          {e.place ? ` · ${e.place}` : ''}
        </span>
      )}
      {e.draft && (
        <>
          {' '}
          <Badge>Draft</Badge>
        </>
      )}
      {e.mine && e.kind === 'Cell' && (
        <>
          {' '}
          <Badge tone="accent">Your cell</Badge>
        </>
      )}
    </>
  );
  const className = `cal-entry kind-${e.kind.toLowerCase()}${e.mine ? ' cal-entry-mine' : ''}`;
  const label = `${kindLabel[e.kind]}: ${e.title} at ${timeOf(e.startsAt)}${e.place ? `, ${e.place}` : ''}`;
  return to ? (
    <Link to={to} className={className} title={label}>
      {body}
    </Link>
  ) : (
    <span className={className} title={label}>
      {body}
    </span>
  );
}
