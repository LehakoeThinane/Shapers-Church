import { space } from '@shapers/tokens';
import { useAudioPlayerStatus } from 'expo-audio';
import { router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';

import { Artwork } from './sermon-bits';
import { AppText, Icon } from './text';
import { getPlayer, stopPlayback, togglePlayback, usePlayerStore } from '@/lib/player';
import { text } from '@/theme/type';

/**
 * What's playing, with play/pause. Sits in the iOS 26 tab bar's accessory slot, or floats above
 * the tab bar elsewhere. Tapping opens the sermon.
 */
export function MiniPlayer({ compact = false }: { compact?: boolean }) {
  const current = usePlayerStore((s) => s.current);
  const status = useAudioPlayerStatus(getPlayer());
  if (!current) return null;

  const fraction = status.duration > 0 ? status.currentTime / status.duration : 0;
  return (
    <View style={styles.bar}>
      <Pressable
        style={styles.info}
        accessibilityRole="button"
        accessibilityLabel={`Now playing ${current.title}. Open sermon.`}
        onPress={() => router.push({ pathname: '/sermon/[slug]', params: { slug: current.slug } })}>
        {!compact && <Artwork uri={current.artworkUrl} title={current.title} size={36} />}
        <View style={styles.flex}>
          <AppText style={text.callout} numberOfLines={1}>
            {current.title}
          </AppText>
          {!compact && (
            <AppText tone="tertiary" style={text.caption} numberOfLines={1}>
              {current.offline ? 'Downloaded · ' : ''}
              {Math.round(fraction * 100)}%
            </AppText>
          )}
        </View>
      </Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel={status.playing ? 'Pause' : 'Play'} onPress={togglePlayback} hitSlop={12}>
        <Icon name={status.playing ? { ios: 'pause.fill', android: 'pause' } : { ios: 'play.fill', android: 'play_arrow' }} />
      </Pressable>
      {!compact && (
        <Pressable accessibilityRole="button" accessibilityLabel="Stop" onPress={stopPlayback} hitSlop={12}>
          <Icon name={{ ios: 'xmark', android: 'close' }} size={18} />
        </Pressable>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', alignItems: 'center', gap: space.md, paddingHorizontal: space.md, paddingVertical: 6 },
  info: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
});
