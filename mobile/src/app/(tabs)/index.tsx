import { radius, space } from '@shapers/tokens';
import { useQuery } from '@tanstack/react-query';
import { Link, router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';

import { Glass } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText, Icon } from '@/components/text';
import { api, unwrap, useSession } from '@/lib/api';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

function greeting(date = new Date()) {
  const hour = date.getHours();
  return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
}

const quickActions = [
  { label: 'Give', icon: { ios: 'heart', android: 'volunteer_activism' }, href: '/give' },
  { label: 'Prayer', icon: { ios: 'hands.sparkles', android: 'self_improvement' }, href: '/community' },
  { label: 'Groups', icon: { ios: 'person.3', android: 'groups' }, href: '/community' },
  { label: 'Serve', icon: { ios: 'hand.raised', android: 'front_hand' }, href: '/community' },
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

      <Glass cornerRadius={radius.card} style={styles.live}>
        <View style={[styles.video, { backgroundColor: palette.color.video.middle }]}>
          <View style={[styles.pill, { backgroundColor: palette.color.accent }]}>
            <AppText style={[text.label, { color: palette.color.text.onAccent }]}>Sunday</AppText>
          </View>
          <AppText style={[serif, styles.videoTitle, { color: palette.color.text.primary }]}>Building productive people</AppText>
        </View>
        <View style={styles.liveMeta}>
          <View style={styles.flex}>
            <AppText style={text.headline}>Next service</AppText>
            <AppText tone="tertiary" style={text.caption}>
              Services will stream here live
            </AppText>
          </View>
          <Link href="/live" asChild>
            <Pressable accessibilityRole="button">
              <AppText tone="interactive" style={text.callout}>
                Open Live
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

      <Section title="Continue watching">
        <Glass style={styles.row}>
          <View style={[styles.thumb, { backgroundColor: palette.color.tile }]}>
            <AppText style={[serif, styles.thumbText]} tone="accent">
              S
            </AppText>
          </View>
          <View style={styles.flex}>
            <AppText style={text.headline}>Sermons arrive in V1</AppText>
            <AppText tone="tertiary" style={text.caption}>
              Audio-first, with downloads for offline listening
            </AppText>
            <View style={[styles.track, { backgroundColor: palette.color.track }]}>
              <View style={[styles.progress, { backgroundColor: palette.appearance === 'dark' ? palette.color.accent : palette.color.interactive }]} />
            </View>
          </View>
        </Glass>
      </Section>

      <Section title="Upcoming">
        <Glass style={styles.card}>
          <AppText tone="secondary">Events and registrations will appear here.</AppText>
        </Glass>
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
  live: { padding: space.sm },
  video: { height: 168, borderRadius: 22, overflow: 'hidden', justifyContent: 'space-between', padding: 12 },
  pill: { alignSelf: 'flex-start', paddingHorizontal: 10, paddingVertical: 5, borderRadius: radius.pill },
  videoTitle: { fontSize: 24 },
  liveMeta: { flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingHorizontal: space.xs, paddingTop: 12, paddingBottom: 4 },
  flex: { flex: 1, gap: 2 },
  quick: { flexDirection: 'row', justifyContent: 'space-between' },
  quickItem: { alignItems: 'center', gap: 7, width: '24%' },
  quickIcon: { width: 58, height: 58, alignItems: 'center', justifyContent: 'center' },
  section: { gap: space.sm },
  row: { flexDirection: 'row', gap: 12, alignItems: 'center', padding: space.sm },
  thumb: { width: 74, height: 74, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center' },
  thumbText: { fontSize: 24 },
  track: { height: 4, borderRadius: 4, overflow: 'hidden', marginTop: 8 },
  progress: { width: '0%', height: '100%', borderRadius: 4 },
  card: { padding: space.lg, gap: space.xxs },
});
