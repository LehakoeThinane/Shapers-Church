import { space } from '@shapers/tokens';
import { Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, Linking, Pressable, StyleSheet, View } from 'react-native';

import { Glass } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText } from '@/components/text';
import { hhmm, servingDay, useRehearse } from '@/lib/serving';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

/** The plan for someone serving: order of service, songs with chart, recording and lyrics, and the team. */
export default function RehearseScreen() {
  const { palette } = useTheme();
  const { id } = useLocalSearchParams<{ id: string }>();
  const plan = useRehearse(id);

  return (
    <Screen>
      <Stack.Screen options={{ title: plan.data?.title ?? 'Plan', headerShown: true, headerTransparent: true, headerTintColor: palette.color.interactive }} />
      <View style={styles.spacer} />
      {plan.isPending ? (
        <ActivityIndicator />
      ) : plan.isError ? (
        <AppText tone="secondary">We couldn't load the plan. Check your connection.</AppText>
      ) : (
        <View style={styles.list}>
          <View>
            <AppText style={text.title}>{plan.data.title}</AppText>
            <AppText tone="secondary">
              {servingDay(plan.data.date)} · {hhmm(plan.data.startTime)}
            </AppText>
          </View>
          {plan.data.notes && (
            <Glass style={styles.card}>
              <AppText tone="accent" style={text.label}>
                For everyone serving
              </AppText>
              <AppText>{plan.data.notes}</AppText>
            </Glass>
          )}

          {plan.data.songs.length > 0 && (
            <>
              <AppText style={text.headline}>Songs</AppText>
              {plan.data.songs.map((s) => (
                <SongCard key={s.itemId} song={s} />
              ))}
            </>
          )}

          <AppText style={text.headline}>Order of service</AppText>
          <Glass style={styles.card}>
            {plan.data.items.map(({ item, startsAt, songTitle }) =>
              item.kind === 'Header' ? (
                <AppText key={item.id} tone="accent" style={[text.label, styles.header]}>
                  {item.title}
                </AppText>
              ) : (
                <View key={item.id} style={styles.row}>
                  <AppText tone="tertiary" style={[text.caption, styles.time]}>
                    {hhmm(startsAt)}
                  </AppText>
                  <View style={styles.flex}>
                    <AppText>{songTitle ?? item.title}</AppText>
                    {(item.leader || item.key) && (
                      <AppText tone="tertiary" style={text.caption}>
                        {[item.key && `Key ${item.key}`, item.leader].filter(Boolean).join(' · ')}
                      </AppText>
                    )}
                  </View>
                  <AppText tone="tertiary" style={text.caption}>
                    {Math.round(item.lengthSeconds / 60)} min
                  </AppText>
                </View>
              ),
            )}
          </Glass>

          <AppText style={text.headline}>Serving</AppText>
          <Glass style={styles.card}>
            {plan.data.team.map((a) => (
              <View key={a.id} style={styles.row}>
                <AppText style={styles.flex}>{a.name}</AppText>
                <AppText tone="tertiary" style={text.caption}>
                  {a.position}
                  {a.status === 'Pending' ? ' (asked)' : ''}
                </AppText>
              </View>
            ))}
          </Glass>
        </View>
      )}
    </Screen>
  );
}

function SongCard({ song: s }: { song: import('@/lib/api').Schemas['RehearseSongDto'] }) {
  const [lyrics, setLyrics] = useState(false);
  const links = [
    s.chartUrl && { label: 'Chord chart', url: s.chartUrl },
    s.audioUrl && { label: 'Recording', url: s.audioUrl },
    s.referenceUrl && { label: 'Learn it', url: s.referenceUrl },
  ].filter((l): l is { label: string; url: string } => !!l);

  return (
    <Glass style={styles.card}>
      <View style={styles.row}>
        <AppText style={[text.headline, styles.flex]}>{s.title}</AppText>
        <AppText tone="accent" style={text.callout}>
          {[s.key && `Key ${s.key}`, s.bpm && `${s.bpm} bpm`].filter(Boolean).join(' · ')}
        </AppText>
      </View>
      {s.author && (
        <AppText tone="tertiary" style={text.caption}>
          {s.author}
        </AppText>
      )}
      {s.notes && <AppText tone="secondary">{s.notes}</AppText>}
      <View style={styles.links}>
        {links.map((l) => (
          <Pressable key={l.label} accessibilityRole="link" onPress={() => void Linking.openURL(l.url)}>
            <AppText tone="interactive" style={text.callout}>
              {l.label}
            </AppText>
          </Pressable>
        ))}
        {s.lyrics && (
          <Pressable accessibilityRole="button" onPress={() => setLyrics(!lyrics)}>
            <AppText tone="interactive" style={text.callout}>
              {lyrics ? 'Hide lyrics' : 'Lyrics'}
            </AppText>
          </Pressable>
        )}
      </View>
      {lyrics && s.lyrics && <AppText>{s.lyrics}</AppText>}
    </Glass>
  );
}

const styles = StyleSheet.create({
  spacer: { height: space.xxl },
  list: { gap: space.md },
  card: { padding: space.lg, gap: space.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
  time: { width: 44 },
  header: { marginTop: space.sm },
  links: { flexDirection: 'row', flexWrap: 'wrap', gap: space.lg },
});
