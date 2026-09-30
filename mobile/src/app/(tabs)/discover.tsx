import { space } from '@shapers/tokens';
import { Link } from 'expo-router';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';

import { Glass } from '@/components/glass';
import { Screen } from '@/components/screen';
import { EventRow } from '@/components/event-bits';
import { Artwork, SeriesTile, SermonRow } from '@/components/sermon-bits';
import { AppText } from '@/components/text';
import { useUpcomingEvents } from '@/lib/events';
import { formatDay, useSeriesList, useSermons } from '@/lib/media';
import { serif, text } from '@/theme/type';

export default function DiscoverScreen() {
  const sermons = useSermons({ pageSize: 12 });
  const series = useSeriesList();
  const events = useUpcomingEvents().data ?? [];
  const [latest, ...rest] = sermons.data?.items ?? [];

  return (
    <Screen>
      <AppText style={text.largeTitle}>Discover</AppText>

      {latest && (
        <Link href={{ pathname: '/sermon/[slug]', params: { slug: latest.slug } }} asChild>
          <Pressable accessibilityRole="button" accessibilityLabel={`Latest sermon: ${latest.title}`}>
            <Glass style={styles.hero}>
              <Artwork uri={latest.thumbnailUrl} title={latest.title} size={120} />
              <View style={styles.flex}>
                <AppText tone="accent" style={text.label}>
                  Latest sermon
                </AppText>
                <AppText style={[serif, styles.heroTitle]} numberOfLines={3}>
                  {latest.title}
                </AppText>
                <AppText tone="tertiary" style={text.caption}>
                  {latest.speakers.join(', ')} · {formatDay(latest.preachedOn)}
                </AppText>
              </View>
            </Glass>
          </Pressable>
        </Link>
      )}

      {(series.data?.length ?? 0) > 0 && (
        <View style={styles.section}>
          <AppText style={text.title}>Series</AppText>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.tiles}>
            {series.data?.map((s) => <SeriesTile key={s.id} series={s} />)}
          </ScrollView>
        </View>
      )}

      {events.length > 0 && (
        <View style={styles.section}>
          <AppText style={text.title}>Events</AppText>
          {events.map((e) => (
            <EventRow key={e.id} event={e} />
          ))}
        </View>
      )}

      {rest.length > 0 && (
        <View style={styles.section}>
          <AppText style={text.title}>Recent sermons</AppText>
          {rest.map((s) => (
            <SermonRow key={s.id} sermon={s} />
          ))}
        </View>
      )}

      {sermons.isError && (
        <Glass style={styles.card}>
          <AppText tone="secondary">You're offline. Downloaded sermons are in your profile.</AppText>
        </Glass>
      )}
      {sermons.data?.items.length === 0 && (
        <Glass style={styles.card}>
          <AppText tone="secondary">Sermons will appear here as they're published.</AppText>
        </Glass>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { flexDirection: 'row', gap: space.md, padding: space.sm, alignItems: 'center' },
  heroTitle: { fontSize: 22, lineHeight: 28 },
  flex: { flex: 1, gap: 4 },
  section: { gap: space.sm },
  tiles: { gap: space.md, paddingRight: space.lg },
  card: { padding: space.lg },
});
