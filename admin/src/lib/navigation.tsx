import type { ReactNode } from 'react';
import type { Access } from './access';
import { Permissions } from './access';
import { allowed, icon, icons } from './products';

/** A page in the menu. Its tabs (sub-pages) show across the top of the page, not in the menu. */
export type NavItem = {
  key: string;
  label: string;
  icon: ReactNode;
  to: string;
  permission?: string | readonly string[] | null;
  tabs?: { to: string; label: string; permission?: string | readonly string[] | null }[];
};

export type NavSection = { title: string; items: NavItem[] };

export const navIcons = {
  home: icon(
    <>
      <path d="M3.5 11 12 4l8.5 7" />
      <path d="M5.5 9.5V20h13V9.5" />
    </>,
  ),
  sermons: icon(
    <>
      <rect x="3.5" y="5" width="17" height="12" rx="2.5" />
      <path d="m10.5 8.8 4 2.2-4 2.2Z" />
      <path d="M8 20.5h8" />
    </>,
  ),
  settings: icon(
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 2.8v2.4M12 18.8v2.4M4.2 7.5l2.1 1.2M17.7 15.3l2.1 1.2M4.2 16.5l2.1-1.2M17.7 8.7l2.1-1.2" />
    </>,
  ),
  bell: icon(
    <>
      <path d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15L6 16Z" />
      <path d="M10 20.5a2 2 0 0 0 4 0" />
    </>,
  ),
  menu: icon(<path d="M4 7h16M4 12h16M4 17h16" />),
  pulse: icon(<path d="M3 12h4l2.5-6 5 12 2.5-6h4" />),
  calendar: icon(
    <>
      <rect x="3.5" y="5" width="17" height="15" rx="2.5" />
      <path d="M3.5 10h17M8 3v4M16 3v4" />
    </>,
  ),
  collapse: icon(<path d="m14 6-6 6 6 6" />),
  expand: icon(<path d="m10 6 6 6-6 6" />),
};

/** The menu, grouped by what people come to do rather than by product. */
export const navSections: NavSection[] = [
  {
    title: 'Church life',
    items: [
      {
        key: 'people',
        label: 'People',
        icon: icons.people,
        to: '/people',
        permission: Permissions.peopleView,
        tabs: [
          { to: '/people', label: 'Everyone' },
          { to: '/connect', label: 'Connect cards' },
          { to: '/duplicates', label: 'Duplicates', permission: Permissions.peopleMerge },
        ],
      },
      {
        key: 'cells',
        label: 'Home cells',
        icon: icons.cells,
        to: '/cells',
        permission: [Permissions.cellsManage, Permissions.cellReportsView],
        tabs: [
          { to: '/cells', label: 'Overview' },
          { to: '/cells/reports', label: 'Reports', permission: Permissions.cellReportsView },
          { to: '/cells/lessons', label: 'Church lessons' },
          { to: '/cells/materials', label: "Leaders' teaching", permission: Permissions.cellReportsView },
        ],
      },
      { key: 'prayer', label: 'Prayer', icon: icons.prayer, to: '/prayer', permission: [Permissions.prayerView, Permissions.prayerModerate] },
      { key: 'events', label: 'Events', icon: icons.registrations, to: '/events', permission: [Permissions.eventsEdit, Permissions.eventsCheckIn] },
      { key: 'calendar', label: 'Calendar', icon: navIcons.calendar, to: '/calendar', permission: null },
    ],
  },
  {
    title: 'Sundays',
    items: [
      {
        key: 'services',
        label: 'Services',
        icon: icons.services,
        to: '/services',
        permission: [Permissions.servicesPlans, Permissions.servicesSchedule, Permissions.servicesSongs],
        tabs: [
          { to: '/services', label: 'Plans' },
          { to: '/services/matrix', label: 'Who’s serving' },
          { to: '/services/teams', label: 'Teams', permission: Permissions.servicesSchedule },
          { to: '/services/songs', label: 'Songs' },
          { to: '/services/types', label: 'Templates', permission: Permissions.servicesPlans },
        ],
      },
      {
        key: 'sermons',
        label: 'Sermons & live',
        icon: navIcons.sermons,
        to: '/sermons',
        permission: [Permissions.mediaEdit, Permissions.livestreamManage],
        tabs: [
          { to: '/sermons', label: 'Sermons', permission: Permissions.mediaEdit },
          { to: '/livestreams', label: 'Livestream', permission: Permissions.livestreamManage },
        ],
      },
      { key: 'chat', label: 'Live chat', icon: icons.chat, to: '/chat', permission: Permissions.chatModerate },
    ],
  },
  {
    title: 'Reach',
    items: [
      {
        key: 'announcements',
        label: 'Announcements',
        icon: icons.announce,
        to: '/announcements',
        permission: [Permissions.announcementsSend, Permissions.announcementsApprove],
      },
      {
        key: 'website',
        label: 'Website & app',
        icon: icons.publishing,
        to: '/content',
        permission: [Permissions.contentEdit, Permissions.contentPublish, Permissions.contentTranslationsReview],
        tabs: [
          { to: '/content', label: 'Pages & posts', permission: [Permissions.contentEdit, Permissions.contentPublish] },
          { to: '/content/translations', label: 'Translations', permission: [Permissions.contentEdit, Permissions.contentTranslationsReview] },
        ],
      },
    ],
  },
];

/** Less often needed: at the bottom of the menu, folded away. */
export const settingsItems: NavItem[] = [
  { key: 'church', label: 'Church setup', icon: icons.campus, to: '/church', permission: null },
  {
    key: 'access',
    label: 'Roles & access',
    icon: icons.access,
    to: '/access',
    permission: [Permissions.usersView, Permissions.auditView],
    tabs: [
      { to: '/access', label: 'Roles & access', permission: Permissions.usersView },
      { to: '/audit', label: 'Audit log', permission: Permissions.auditView },
    ],
  },
  {
    key: 'privacy',
    label: 'Privacy (POPIA)',
    icon: icons.shield,
    to: '/privacy',
    permission: [Permissions.privacyRequests, Permissions.privacyBreaches],
    tabs: [
      { to: '/privacy', label: 'Requests', permission: Permissions.privacyRequests },
      { to: '/breaches', label: 'Breach register', permission: Permissions.privacyBreaches },
    ],
  },
  { key: 'assist', label: 'AI help', icon: icons.spark, to: '/assist', permission: Permissions.assistUsage },
  { key: 'background', label: 'Background work', icon: navIcons.pulse, to: '/background-work', permission: Permissions.jobsView },
];

const all = () => [...navSections.flatMap((s) => s.items), ...settingsItems];

const matches = (pathname: string, to: string) => pathname === to || pathname.startsWith(`${to}/`);

/** The menu item a page belongs to: its own address or one of its tabs, the longest match winning. */
export function navItemFor(pathname: string): NavItem | undefined {
  let best: { item: NavItem; length: number } | undefined;
  for (const item of all()) {
    for (const to of [item.to, ...(item.tabs ?? []).map((t) => t.to)]) {
      if (matches(pathname, to) && (!best || to.length > best.length)) best = { item, length: to.length };
    }
  }

  return best?.item;
}

export function visibleTabs(item: NavItem | undefined, access: Access | undefined) {
  return (item?.tabs ?? []).filter((t) => allowed(access, t.permission === undefined ? item!.permission : t.permission));
}

/** The tab a page belongs to: the longest tab address it starts with. */
export function currentTab(pathname: string, tabs: { to: string }[]) {
  return tabs.filter((t) => matches(pathname, t.to)).sort((a, b) => b.to.length - a.to.length)[0]?.to;
}
