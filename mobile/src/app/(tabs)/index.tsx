import { radius, space } from '@shapers/tokens';
import { useQuery } from '@tanstack/react-query';
import { Link, router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';

import { Glass } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText, Icon } from '@/components/text';
import { EventRow } from '@/components/event-bits';
import { SermonRow } from '@/components/sermon-bits';
import { api, unwrap, useSession } from '@/lib/api';
import { useNews } from '@/lib/content';
import { eventWhen, useMyRegistrations, useUpcomingEvents } from '@/lib/events';
import { enablePush, pushSupported, useInbox, usePushPermission, usePushStore } from '@/lib/notifications';
import { startsIn, useLiveNow } from '@/lib/live';
import { useContinueListening, useSermons } from '@/lib/media';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

function greeting(date = new Date()) {
  const hour = date.getHours();
  return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
}

const quickActions = [
  { label: 'Give', icon: { ios: 'heart', android: 'volunteer_activism' }, href: '/give' },
  { label: 'Prayer', icon: { ios: 'hands.sparkles', android: 'self_improvement' }, href: '/prayer' },
  { label: 'Groups', icon: { ios: 'person.3', android: 'groups' }, href: '/community' },
  { label: 'Serve', icon: { ios: 'hand.raised', android: 'front_hand' }, href: '/community' },
  { label: 'Calendar', icon: { ios: 'calendar', android: 'calendar_month' }, href: '/calendar' },
  { label: 'Kids', icon: { ios: 'figure.and.child.holdinghands', android: 'child_care' }, href: '/kids' },
] as const;

export default function HomeScreen() {
  const { palette } = useTheme();
  const status = useSession((s) => s.status);
  const profile = useQuery({
    queryKey: ['me', 'profile'],
    queryFn: async () => unwrap(await api.GET('/api/me/profile')),
    enabled: status === 'signedIn',
  });
  const church = useQuery({ queryKey: ['church'], queryFn: async () => unwrap(await api.GET('/api/church')) });
  const latest = useSermons({ pageSize: 1 }).data?.items[0];
  const continueListening = useContinueListening().data ?? [];
  const live = useLiveNow().data;
  const isLive = live?.state === 'Live';
  const stream = live?.stream;
  const events = useUpcomingEvents().data ?? [];
  const news = useNews(3).data ?? [];
  const myNext = useMyRegistrations().upcoming[0];
  const unread = useInbox().data?.unread ?? 0;
  const permission = usePushPermission();
  const { dismissed, dismiss } = usePushStore();
  const askForPush = status === 'signedIn' && pushSupported && permission.data === 'undetermined' && !dismissed;

  const firstName = profile.data?.preferredName ?? profile.data?.firstName;
  const initials = profile.data ? `${profile.data.firstName[0] ?? ''}${profile.data.lastName[0] ?? ''}` : '';

  return (
    <Screen>
      <View style={styles.top}>
        <View>
          <AppText tone="tertiary" style={text.callout}>
            {greeting()}
          </AppText>
          <AppText style={text.largeTitle}>{firstName ?? 'Welcome'}</AppText>
        </View>
        <View style={styles.topActions}>
          {status === 'signedIn' && (
            <Pressable accessibilityRole="button" accessibilityLabel={unread ? `Notifications, ${unread} unread` : 'Notifications'} onPress={() => router.push('/inbox')}>
              <Glass cornerRadius={21} style={styles.avatar}>
                <Icon name={{ ios: 'bell', android: 'notifications' }} size={20} />
                {unread > 0 && (
                  <View style={[styles.badge, { backgroundColor: palette.color.accent }]}>
                    <AppText style={[text.label, { color: palette.color.text.onAccent }]}>{unread > 9 ? '9+' : unread}</AppText>
                  </View>
                )}
              </Glass>
            </Pressable>
          )}
          <Pressable accessibilityRole="button" accessibilityLabel="Profile" onPress={() => router.push('/profile')}>
            <Glass cornerRadius={21} style={styles.avatar}>
              {initials ? (
                <AppText tone="interactive" style={text.headline}>
                  {initials}
                </AppText>
              ) : (
                <Icon name={{ ios: 'person.fill', android: 'person' }} size={20} />
              )}
            </Glass>
          </Pressable>
        </View>
      </View>

      {askForPush && (
        <Glass style={styles.card}>
          <AppText style={text.headline}>Know when we go live</AppText>
          <AppText tone="secondary">
            Get a notification when a service starts, a new sermon is published, or your event booking changes. You choose which in your profile.
          </AppText>
          <View style={styles.promptActions}>
            <Pressable accessibilityRole="button" onPress={() => void enablePush().finally(() => void permission.refetch())}>
              <AppText tone="interactive" style={text.headline}>
                Turn on
              </AppText>
            </Pressable>
            <Pressable accessibilityRole="button" onPress={dismiss}>
              <AppText tone="tertiary" style={text.callout}>
                Not now
              </AppText>
            </Pressable>
          </View>
        </Glass>
      )}

      <Glass cornerRadius={radius.card} style={styles.live}>
        <View style={[styles.video, { backgroundColor: palette.color.video.middle }]}>
          <View style={[styles.pill, { backgroundColor: palette.color.accent }]}>
            <AppText style={[text.label, { color: palette.color.text.onAccent }]}>{isLive ? '● Live now' : 'Sunday'}</AppText>
          </View>
          <AppText style={[serif, styles.videoTitle, { color: palette.color.text.primary }]}>{stream?.title ?? 'Building productive people'}</AppText>
        </View>
        <View style={styles.liveMeta}>
          <View style={styles.flex}>
            <AppText style={text.headline}>{isLive ? 'Watch the service' : 'Next service'}</AppText>
            <AppText tone="tertiary" style={text.caption}>
              {isLive ? 'Notes, scripture and Give are in the Live tab' : stream ? startsIn(stream.scheduledStart) : 'Services will stream here live'}
            </AppText>
          </View>
          <Link href="/live" asChild>
            <Pressable accessibilityRole="button">
              <AppText tone="interactive" style={text.callout}>
                {isLive ? 'Watch now' : 'Open Live'}
              </AppText>
            </Pressable>
          </Link>
        </View>
      </Glass>

      <View style={styles.quick}>
        {quickActions.map((action) => (
          <Link key={action.label} href={action.href} asChild>
            <Pressable accessibilityRole="button" style={styles.quickItem}>
              <Glass cornerRadius={20} interactive style={styles.quickIcon}>
                <Icon name={action.icon} />
              </Glass>
              <AppText tone="secondary" style={text.caption}>
                {action.label}
              </AppText>
            </Pressable>
          </Link>
        ))}
      </View>

      {continueListening.length > 0 ? (
        <Section title="Continue listening">
          {continueListening.slice(0, 3).map((c) => (
            <SermonRow key={c.sermon.id} sermon={c.sermon} progressSeconds={c.positionSeconds} />
          ))}
        </Section>
      ) : (
        latest && (
          <Section title="Latest sermon">
            <SermonRow sermon={latest} />
          </Section>
        )
      )}

      {myNext && (
        <Link href="/tickets" asChild>
          <Pressable accessibilityRole="button" accessibilityLabel={`Your ticket for ${myNext.eventTitle}`}>
            <Glass style={styles.ticket}>
              <Icon name={{ ios: 'ticket', android: 'confirmation_number' }} />
              <View style={styles.flex}>
                <AppText style={text.headline} numberOfLines={1}>
                  {myNext.eventTitle}
                </AppText>
                <AppText tone="tertiary" style={text.caption}>
                  {myNext.status === 'Waitlisted' ? 'Waiting list' : eventWhen(myNext.startsAt)}
                </AppText>
              </View>
              <AppText tone="interactive" style={text.callout}>
                My tickets
              </AppText>
            </Glass>
          </Pressable>
        </Link>
      )}

      {news.length > 0 && (
        <Section title="News">
          {news.map((n) => (
            <Link key={n.id} href={{ pathname: '/post/[slug]', params: { slug: n.slug } }} asChild>
              <Pressable accessibilityRole="button" accessibilityLabel={n.title}>
                <Glass style={styles.card}>
                  <AppText style={text.headline}>{n.title}</AppText>
                  {n.summary && (
                    <AppText tone="secondary" numberOfLines={2}>
                      {n.summary}
                    </AppText>
                  )}
                </Glass>
              </Pressable>
            </Link>
          ))}
        </Section>
      )}

      <Section title="Upcoming">
        {events.length > 0 ? (
          events.slice(0, 3).map((e) => <EventRow key={e.id} event={e} />)
        ) : (
          <Glass style={styles.card}>
            <AppText tone="secondary">No events coming up right now.</AppText>
          </Glass>
        )}
      </Section>

      <Section title="My church">
        <Glass style={styles.card}>
          <AppText style={text.headline}>{church.data?.organisation.name ?? 'Shapers Church'}</AppText>
          {church.data?.campuses.map((c) => (
            <AppText key={c.id} tone="secondary">
              {c.name} campus
            </AppText>
          ))}
          {church.data?.organisation.contactEmail && (
            <AppText tone="interactive" style={text.callout}>
              {church.data.organisation.contactEmail}
            </AppText>
          )}
          {church.isError && (
            <AppText tone="tertiary" style={text.caption}>
              Offline. Church details will load when you're connected.
            </AppText>
          )}
        </Glass>
      </Section>
    </Screen>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <AppText style={text.title}>{title}</AppText>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: space.xxs },
  avatar: { width: 42, height: 42, alignItems: 'center', justifyContent: 'center' },
  topActions: { flexDirection: 'row', gap: space.sm },
  badge: { position: 'absolute', top: -4, right: -4, minWidth: 18, height: 18, borderRadius: 9, paddingHorizontal: 4, alignItems: 'center', justifyContent: 'center' },
  promptActions: { flexDirection: 'row', alignItems: 'center', gap: space.lg, marginTop: space.xs },
  live: { padding: space.sm },
  video: { height: 168, borderRadius: 22, overflow: 'hidden', justifyContent: 'space-between', padding: 12 },
  pill: { alignSelf: 'flex-start', paddingHorizontal: 10, paddingVertical: 5, borderRadius: radius.pill },
  videoTitle: { fontSize: 24 },
  liveMeta: { flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingHorizontal: space.xs, paddingTop: 12, paddingBottom: 4 },
  flex: { flex: 1, gap: 2 },
  quick: { flexDirection: 'row', flexWrap: 'wrap', rowGap: space.md },
  quickItem: { alignItems: 'center', gap: 7, width: '25%' },
  quickIcon: { width: 58, height: 58, alignItems: 'center', justifyContent: 'center' },
  section: { gap: space.sm },
  card: { padding: space.lg, gap: space.xxs },
  ticket: { flexDirection: 'row', alignItems: 'center', gap: space.md, padding: space.md },
});
