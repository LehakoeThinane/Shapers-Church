import { useQuery } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from './api';

export type Access = Schemas['MyAccessDto'];

/** Who is signed in and what works in this session (sensitive permissions are absent without 2FA). */
export function useAccess() {
  return useQuery({
    queryKey: ['me', 'access'],
    queryFn: async () => unwrap(await api.GET('/api/me/access')),
    staleTime: 60_000,
    retry: false,
  });
}

export function can(access: Access | undefined, permission: string): boolean {
  return !!access?.permissions.some((p) => p.permission === permission);
}

export function scopesFor(access: Access | undefined, permission: string): string[] {
  return access?.permissions.find((p) => p.permission === permission)?.scopes ?? [];
}

/** True when a grant at `grantScope` reaches `scope` (same path or an ancestor). */
export function covers(grantScope: string, scope: string): boolean {
  return scope === grantScope || scope.startsWith(`${grantScope}.`);
}

export const Permissions = {
  peopleView: 'people.profiles.view',
  peopleEdit: 'people.profiles.edit',
  peopleMerge: 'people.profiles.merge',
  statusesManage: 'people.statuses.manage',
  campusesManage: 'church.campuses.manage',
  ministriesManage: 'church.ministries.manage',
  usersView: 'identity.users.view',
  rolesManage: 'identity.roles.manage',
  grantsManage: 'identity.grants.manage',
  auditView: 'platform.audit.view',
  mediaEdit: 'media.sermons.edit',
  mediaPublish: 'media.sermons.publish',
  speakersManage: 'media.speakers.manage',
  livestreamManage: 'media.livestream.manage',
  chatModerate: 'media.chat.moderate',
  eventsEdit: 'events.edit',
  eventsPublish: 'events.publish',
  eventsRegistrationsView: 'events.registrations.view',
  eventsRegistrationsManage: 'events.registrations.manage',
  eventsCheckIn: 'events.checkin',
  prayerView: 'prayer.requests.view',
  prayerModerate: 'prayer.requests.moderate',
  announcementsSend: 'communications.announcements.send',
  announcementsApprove: 'communications.announcements.approve',
  privacyRequests: 'privacy.requests.manage',
  privacyBreaches: 'privacy.breaches.manage',
  contentEdit: 'content.edit',
  contentPublish: 'content.publish',
  contentTranslationsReview: 'content.translations.review',
  cellsManage: 'groups.cells.manage',
  cellReportsView: 'groups.reports.view',
  servicesPlans: 'services.plans.edit',
  servicesSchedule: 'services.schedule',
  servicesSongs: 'services.songs.edit',
  assistDrafts: 'assist.drafts.create',
  assistUsage: 'assist.usage.view',
} as const;
