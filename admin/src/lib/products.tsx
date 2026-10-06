import type { ReactNode } from 'react';
import { can, Permissions, type Access } from './access';

/**
 * The admin portal's products, grouped like Planning Center. One list drives the Home grid and the sidebar.
 * A product shows when the user holds any of its permissions (null: everyone signed in).
 */
export type Product = {
  key: string;
  name: string;
  description: string;
  icon: ReactNode;
  status: 'live' | 'soon';
  /** First page of the product. */
  to?: string;
  permission?: string | readonly string[] | null;
  /** The product's own menu. Items without a permission inherit the product's. */
  links?: { to: string; label: string; permission?: string | readonly string[] | null }[];
};

export type ProductGroup = { title: string; products: Product[] };

const icon = (path: ReactNode) => (
  <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    {path}
  </svg>
);

const icons = {
  people: icon(
    <>
      <circle cx="9" cy="8" r="3.2" />
      <path d="M3 19c0-3.3 2.7-5.5 6-5.5s6 2.2 6 5.5" />
      <path d="M16 5.2a3 3 0 0 1 0 5.6M18 13.8c1.8.6 3 2.3 3 4.7" />
    </>,
  ),
  cells: icon(
    <>
      <path d="M3 11.5 12 4l9 7.5" />
      <path d="M5.5 9.5V20h13V9.5" />
      <circle cx="12" cy="13.5" r="2" />
      <path d="M9 19c0-1.7 1.3-2.8 3-2.8s3 1.1 3 2.8" />
    </>,
  ),
  announce: icon(
    <>
      <path d="M4 10v4h3l6 4V6L7 10H4Z" />
      <path d="M17 9a4 4 0 0 1 0 6" />
    </>,
  ),
  prayer: icon(<path d="M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10Z" />),
  services: icon(
    <>
      <path d="M8 6h12M8 12h12M8 18h12" />
      <circle cx="4" cy="6" r="1" />
      <circle cx="4" cy="12" r="1" />
      <circle cx="4" cy="18" r="1" />
    </>,
  ),
  music: icon(
    <>
      <path d="M9 18V5l11-2v13" />
      <circle cx="6" cy="18" r="3" />
      <circle cx="17" cy="16" r="3" />
    </>,
  ),
  giving: icon(
    <>
      <path d="M12 21s-8-4.6-8-11a4.5 4.5 0 0 1 8-2.8A4.5 4.5 0 0 1 20 10c0 6.4-8 11-8 11Z" />
      <path d="M12 9v6M9.5 12h5" />
    </>,
  ),
  calendar: icon(
    <>
      <rect x="3.5" y="5" width="17" height="15" rx="2.5" />
      <path d="M3.5 10h17M8 3v4M16 3v4" />
    </>,
  ),
  registrations: icon(
    <>
      <path d="M4 7.5h16v3a2 2 0 0 0 0 4v3H4v-3a2 2 0 0 0 0-4v-3Z" />
      <path d="M14 7.5v10" strokeDasharray="2 2" />
    </>,
  ),
  checkins: icon(
    <>
      <rect x="3.5" y="3.5" width="17" height="17" rx="4" />
      <path d="m8 12.5 3 3 5-6" />
    </>,
  ),
  publishing: icon(
    <>
      <rect x="3.5" y="4" width="17" height="16" rx="2.5" />
      <path d="M3.5 9h17M8 13h8M8 16.5h5" />
    </>,
  ),
  chat: icon(<path d="M5 18.5 4 21l3.5-1.5A8.5 8.5 0 1 0 4 12.5c0 2.2.8 4.2 2 5.6" />),
  app: icon(
    <>
      <rect x="6.5" y="2.5" width="11" height="19" rx="2.5" />
      <path d="M10.5 18.5h3" />
    </>,
  ),
  access: icon(
    <>
      <circle cx="8" cy="14" r="4" />
      <path d="M11 11 20 2M16.5 5.5 19 8M14.5 7.5 16.5 9.5" />
    </>,
  ),
  shield: icon(
    <>
      <path d="M12 3 4.5 6v5.5c0 4.6 3.2 8 7.5 9.5 4.3-1.5 7.5-4.9 7.5-9.5V6L12 3Z" />
      <path d="m9 12 2 2 4-4" />
    </>,
  ),
  spark: icon(
    <>
      <path d="M12 3v4M12 17v4M3 12h4M17 12h4" />
      <path d="m6.3 6.3 2.1 2.1M15.6 15.6l2.1 2.1M6.3 17.7l2.1-2.1M15.6 8.4l2.1-2.1" />
    </>,
  ),
  campus: icon(
    <>
      <path d="M3 21h18M5 21V10l7-5 7 5v11" />
      <path d="M10 21v-5h4v5" />
    </>,
  ),
};

export const cellsIcon = icons.cells;

export const productGroups: ProductGroup[] = [
  {
    title: 'People & communication',
    products: [
      {
        key: 'people',
        name: 'People',
        description: 'Membership database, households and follow-up',
        icon: icons.people,
        status: 'live',
        to: '/people',
        permission: Permissions.peopleView,
        links: [
          { to: '/people', label: 'People' },
          { to: '/connect', label: 'Connect cards' },
          { to: '/duplicates', label: 'Duplicates', permission: Permissions.peopleMerge },
        ],
      },
      {
        key: 'cells',
        name: 'Cells',
        description: 'Home cells: leaders, meetings, reports and teaching',
        icon: icons.cells,
        status: 'live',
        to: '/cells',
        permission: [Permissions.cellsManage, Permissions.cellReportsView],
        links: [
          { to: '/cells', label: 'Overview' },
          { to: '/cells/reports', label: 'Reports', permission: Permissions.cellReportsView },
          { to: '/cells/lessons', label: 'Church lessons' },
          { to: '/cells/materials', label: "Leaders' teaching", permission: Permissions.cellReportsView },
        ],
      },
      {
        key: 'announcements',
        name: 'Announcements',
        description: 'News by app, push and email, with approvals',
        icon: icons.announce,
        status: 'live',
        to: '/announcements',
        permission: [Permissions.announcementsSend, Permissions.announcementsApprove],
      },
      {
        key: 'prayer',
        name: 'Prayer',
        description: 'Prayer requests and the moderated prayer wall',
        icon: icons.prayer,
        status: 'live',
        to: '/prayer',
        permission: [Permissions.prayerView, Permissions.prayerModerate],
      },
    ],
  },
  {
    title: 'Worship & teams',
    products: [
      { key: 'services', name: 'Services', description: 'Worship planning, run sheets and team rosters', icon: icons.services, status: 'soon' },
      { key: 'music', name: 'Music stand', description: 'Song charts and keys for the band', icon: icons.music, status: 'soon' },
    ],
  },
  {
    title: 'Donations',
    products: [{ key: 'giving', name: 'Giving', description: 'Tithes, offerings, statements and 18A certificates', icon: icons.giving, status: 'soon' }],
  },
  {
    title: 'Events',
    products: [
      { key: 'calendar', name: 'Calendar', description: 'Rooms, facilities and resources', icon: icons.calendar, status: 'soon' },
      {
        key: 'registrations',
        name: 'Registrations',
        description: 'Events, bookings, tickets and waiting lists',
        icon: icons.registrations,
        status: 'live',
        to: '/events',
        permission: [Permissions.eventsEdit, Permissions.eventsCheckIn],
      },
      { key: 'checkins', name: 'Check-ins', description: 'Kids check-in with security codes and labels', icon: icons.checkins, status: 'soon' },
    ],
  },
  {
    title: 'Mobile app & website',
    products: [
      {
        key: 'publishing',
        name: 'Publishing',
        description: 'Website pages, blog, sermons and livestream',
        icon: icons.publishing,
        status: 'live',
        to: '/content',
        permission: [Permissions.contentEdit, Permissions.contentPublish, Permissions.contentTranslationsReview, Permissions.mediaEdit, Permissions.livestreamManage],
        links: [
          { to: '/content', label: 'Pages & posts', permission: [Permissions.contentEdit, Permissions.contentPublish] },
          { to: '/content/translations', label: 'Translations', permission: [Permissions.contentEdit, Permissions.contentTranslationsReview] },
          { to: '/sermons', label: 'Sermons', permission: Permissions.mediaEdit },
          { to: '/livestreams', label: 'Livestream', permission: Permissions.livestreamManage },
        ],
      },
      {
        key: 'chat',
        name: 'Live chat',
        description: 'Moderate the chat during services',
        icon: icons.chat,
        status: 'live',
        to: '/chat',
        permission: Permissions.chatModerate,
      },
      { key: 'staff-app', name: 'Staff app', description: 'Tasks and check-in on your phone', icon: icons.app, status: 'soon' },
    ],
  },
  {
    title: 'System tools',
    products: [
      {
        key: 'church',
        name: 'Church setup',
        description: 'Campuses and ministries',
        icon: icons.campus,
        status: 'live',
        to: '/church',
        permission: null,
      },
      {
        key: 'access',
        name: 'Roles & access',
        description: 'Who can do what, and where',
        icon: icons.access,
        status: 'live',
        to: '/access',
        permission: [Permissions.usersView, Permissions.auditView],
        links: [
          { to: '/access', label: 'Roles & access', permission: Permissions.usersView },
          { to: '/audit', label: 'Audit log', permission: Permissions.auditView },
        ],
      },
      {
        key: 'assist',
        name: 'AI help',
        description: 'What AI drafts for staff, and what it costs',
        icon: icons.spark,
        status: 'live',
        to: '/assist',
        permission: Permissions.assistUsage,
      },
      {
        key: 'privacy',
        name: 'Privacy (POPIA)',
        description: 'Data requests and the breach register',
        icon: icons.shield,
        status: 'live',
        to: '/privacy',
        permission: [Permissions.privacyRequests, Permissions.privacyBreaches],
        links: [
          { to: '/privacy', label: 'Privacy requests', permission: Permissions.privacyRequests },
          { to: '/breaches', label: 'Breach register', permission: Permissions.privacyBreaches },
        ],
      },
    ],
  },
];

export function allowed(access: Access | undefined, permission: string | readonly string[] | null | undefined): boolean {
  if (permission === null || permission === undefined) return true;
  return (typeof permission === 'string' ? [permission] : permission).some((p) => can(access, p));
}

/** Live products this user can open. */
export function openProducts(access: Access | undefined): Product[] {
  return productGroups.flatMap((g) => g.products).filter((p) => p.status === 'live' && allowed(access, p.permission));
}

/** The product a path belongs to, for highlighting it and showing its menu. */
export function productFor(pathname: string): Product | undefined {
  const all = productGroups.flatMap((g) => g.products).filter((p) => p.status === 'live');
  const matches = (to: string) => pathname === to || pathname.startsWith(`${to}/`);
  return all.find((p) => (p.links ?? (p.to ? [{ to: p.to }] : [])).some((l) => matches(l.to)));
}
