import { space } from '@shapers/tokens';
import { Stack, useLocalSearchParams } from 'expo-router';
import { StyleSheet, View } from 'react-native';

import { Screen } from '@/components/screen';
import { Artwork, SermonRow } from '@/components/sermon-bits';
import { AppText } from '@/components/text';
import { useSeries, useSermons } from '@/lib/media';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

export default function SeriesScreen() {
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const { palette } = useTheme();
  const series = useSeries(slug);
  const sermons = useSermons({ series: slug, pageSize: 50 });

  return (
    <Screen>
      <Stack.Screen options={{ title: '', headerTransparent: true, headerTintColor: palette.color.interactive }} />
      {series.data && (
        <View style={styles.header}>
          <Artwork uri={series.data.artworkUrl} title={series.data.title} size={160} />
          <AppText style={[serif, styles.title]}>{series.data.title}</AppText>
          {series.data.description && <AppText tone="secondary">{series.data.description}</AppText>}
        </View>
      )}
      <View style={styles.list}>
        {/* Oldest first, so a series reads in the order it was preached. */}
        {[...(sermons.data?.items ?? [])].reverse().map((s) => (
          <SermonRow key={s.id} sermon={s} />
        ))}
      </View>
      {series.isError && <AppText tone="secondary">We couldn't load this series. Check your connection.</AppText>}
      {sermons.data?.items.length === 0 && (
        <AppText tone="tertiary" style={text.caption}>
          No sermons in this series yet.
        </AppText>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: { alignItems: 'center', gap: space.sm, marginTop: 40 },
  title: { fontSize: 28, textAlign: 'center' },
  list: { gap: space.sm },
});
