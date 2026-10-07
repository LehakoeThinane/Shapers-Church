import { space } from '@shapers/tokens';
import { router, Stack, type Href } from 'expo-router';
import { ActivityIndicator, Linking, Pressable, StyleSheet, View } from 'react-native';

import { Glass, PrimaryButton } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText } from '@/components/text';
import { useSession } from '@/lib/api';
import { calendarFeedUrl, dayHeading, dayKey, timeOf, useCalendar, type CalendarEntry } from '@/lib/calendar';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

const kindLabel: Record<CalendarEntry['kind'], string> = {
  Event: 'Event',
  Livestream: 'Online',
  Service: 'Service',
  Cell: 'Your home cell',
  Serving: "You're serving",
};

/** Where an entry opens in the app, if it has a screen of its own. */
function hrefFor(e: CalendarEntry): Href | null {
  if (e.kind === 'Event' && e.slug) return { pathname: '/event/[slug]', params: { slug: e.slug } };
  if (e.kind === 'Livestream') return '/live';
  if (e.kind === 'Serving') return '/serving';
  return null;
}

/** What's on in the next eight weeks, day by day, with your own cell and serving dates marked. */
export default function CalendarScreen() {
  const { palette } = useTheme();
  const status = useSession((s) => s.status);
  const calendar = useCalendar();
  const today = dayKey(new Date().toISOString());

  const days = new Map<string, CalendarEntry[]>();
  for (const e of calendar.data ?? []) days.set(dayKey(e.startsAt), [...(days.get(dayKey(e.startsAt)) ?? []), e]);

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Calendar', headerShown: true, headerTransparent: true, headerTintColor: palette.color.interactive }} />
      <View style={styles.spacer} />

      <Glass style={styles.card}>
        <AppText style={text.headline}>What's on</AppText>
        <AppText tone="secondary">
          {status === 'signedIn'
            ? 'Church events and livestreams, with your home cell and serving dates marked.'
            : 'Church events and livestreams. Sign in to see your home cell and serving dates too.'}
        </AppText>
        <PrimaryButton label="Add to my calendar" onPress={() => void Linking.openURL(calendarFeedUrl)} />
        <AppText tone="tertiary" style={text.caption}>
          Your phone's calendar keeps church events up to date. Your cell and serving dates stay in the app.
        </AppText>
      </Glass>

      {calendar.isPending && <ActivityIndicator />}
      {calendar.isError && <AppText tone="secondary">We couldn't load the calendar. Check your connection and try again.</AppText>}
      {calendar.data && days.size === 0 && (
        <Glass style={styles.card}>
          <AppText tone="secondary">Nothing on the calendar in the next eight weeks yet. New events appear here as soon as they're published.</AppText>
        </Glass>
      )}

      {[...days.entries()].map(([key, entries]) => (
        <View key={key} style={styles.day}>
          <AppText tone={key === today ? 'accent' : 'secondary'} style={text.label} accessibilityRole="header">
            {key === today ? `Today, ${dayHeading(key)}` : dayHeading(key)}
          </AppText>
          {entries.map((e) => (
            <EntryRow key={`${e.kind}-${e.refId}-${e.startsAt}`} entry={e} />
          ))}
        </View>
      ))}
    </Screen>
  );
}

function EntryRow({ entry: e }: { entry: CalendarEntry }) {
  const { palette } = useTheme();
  const href = hrefFor(e);
  const row = (
    <Glass style={[styles.entry, e.mine && { backgroundColor: palette.color.glass.fillActive }]}>
      <AppText tone="accent" style={[text.callout, styles.time]}>
        {timeOf(e.startsAt)}
      </AppText>
      <View style={styles.flex}>
        <AppText style={text.headline} numberOfLines={2}>
          {e.title}
        </AppText>
        <AppText tone="tertiary" style={text.caption}>
          {kindLabel[e.kind]}
          {e.place ? ` · ${e.place}` : ''}
        </AppText>
      </View>
    </Glass>
  );
  return href ? (
    <Pressable accessibilityRole="button" accessibilityLabel={`${e.title}, ${timeOf(e.startsAt)}`} onPress={() => router.push(href)}>
      {row}
    </Pressable>
  ) : (
    row
  );
}

const styles = StyleSheet.create({
  spacer: { height: space.xxl },
  card: { padding: space.lg, gap: space.sm },
  day: { gap: space.sm },
  entry: { flexDirection: 'row', alignItems: 'center', gap: space.md, padding: space.md },
  time: { width: 48 },
  flex: { flex: 1 },
});
