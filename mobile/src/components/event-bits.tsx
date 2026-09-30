import { radius, space } from '@shapers/tokens';
import { Link } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';

import { Glass } from './glass';
import { AppText } from './text';
import { eventTime, seatsNote, type ChurchEvent } from '@/lib/events';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

const monthFormat = new Intl.DateTimeFormat('en-ZA', { month: 'short' });

/** Date block: the day in large type, the month above it. */
export function DateBadge({ iso }: { iso: string }) {
  const { palette } = useTheme();
  const d = new Date(iso);
  return (
    <View style={[styles.badge, { backgroundColor: palette.color.glass.fill, borderColor: palette.color.glass.edge }]}>
      <AppText tone="accent" style={text.label}>
        {monthFormat.format(d).toUpperCase()}
      </AppText>
      <AppText style={text.title}>{d.getDate()}</AppText>
    </View>
  );
}

export function EventRow({ event }: { event: ChurchEvent }) {
  const note = seatsNote(event);
  return (
    <Link href={{ pathname: '/event/[slug]', params: { slug: event.slug } }} asChild>
      <Pressable accessibilityRole="button" accessibilityLabel={`${event.title}, ${new Date(event.startsAt).toDateString()}`}>
        <Glass style={styles.row}>
          <DateBadge iso={event.startsAt} />
          <View style={styles.flex}>
            <AppText style={text.headline} numberOfLines={2}>
              {event.title}
            </AppText>
            <AppText tone="tertiary" style={text.caption} numberOfLines={1}>
              {eventTime(event.startsAt)}
              {event.location ? ` · ${event.location.name}` : ''}
            </AppText>
            {note && (
              <AppText tone={event.registrationOpen ? 'interactive' : 'tertiary'} style={text.caption}>
                {note}
              </AppText>
            )}
          </View>
        </Glass>
      </Pressable>
    </Link>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: space.md, padding: space.sm },
  flex: { flex: 1, gap: 2 },
  badge: { width: 56, height: 60, borderRadius: radius.sm, borderWidth: StyleSheet.hairlineWidth, alignItems: 'center', justifyContent: 'center' },
});
