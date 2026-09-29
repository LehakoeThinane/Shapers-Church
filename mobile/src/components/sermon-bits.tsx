import { radius, space } from '@shapers/tokens';
import { Image } from 'expo-image';
import { Link } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';

import { Glass } from './glass';
import { AppText, Icon } from './text';
import { formatMinutes, useMediaSettings, type SermonSummary, type Series } from '@/lib/media';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

/** Artwork, or a quiet serif initial when there's none (or low-data mode is on). */
export function Artwork({ uri, title, size }: { uri: string | null | undefined; title: string; size: number }) {
  const { palette } = useTheme();
  const lowData = useMediaSettings((s) => s.lowData);
  return (
    <View style={[styles.art, { width: size, height: size, backgroundColor: palette.color.tile }]}>
      {uri && !lowData ? (
        <Image source={uri} style={StyleSheet.absoluteFill} contentFit="cover" transition={150} cachePolicy="disk" accessibilityIgnoresInvertColors />
      ) : (
        <AppText tone="accent" style={[serif, { fontSize: size / 3 }]}>
          {title.slice(0, 1)}
        </AppText>
      )}
    </View>
  );
}

export function SermonRow({ sermon, progressSeconds }: { sermon: SermonSummary; progressSeconds?: number }) {
  const { palette } = useTheme();
  const meta = [sermon.speakers.join(', '), formatMinutes(sermon.audioDurationSeconds)].filter(Boolean).join(' · ');
  const fraction = progressSeconds && sermon.audioDurationSeconds ? Math.min(1, progressSeconds / sermon.audioDurationSeconds) : null;
  return (
    <Link href={{ pathname: '/sermon/[slug]', params: { slug: sermon.slug } }} asChild>
      <Pressable accessibilityRole="button" accessibilityLabel={`${sermon.title}, ${meta}`}>
        <Glass style={styles.row}>
          <Artwork uri={sermon.thumbnailUrl} title={sermon.title} size={74} />
          <View style={styles.flex}>
            <AppText style={text.headline} numberOfLines={2}>
              {sermon.title}
            </AppText>
            <AppText tone="tertiary" style={text.caption} numberOfLines={1}>
              {sermon.scripture[0] ? `${sermon.scripture[0]} · ` : ''}
              {meta}
            </AppText>
            {fraction !== null && (
              <View style={[styles.track, { backgroundColor: palette.color.track }]}>
                <View
                  style={[
                    styles.progress,
                    { width: `${Math.round(fraction * 100)}%`, backgroundColor: palette.appearance === 'dark' ? palette.color.accent : palette.color.interactive },
                  ]}
                />
              </View>
            )}
          </View>
          <Icon name={{ ios: 'chevron.right', android: 'chevron_right' }} size={16} />
        </Glass>
      </Pressable>
    </Link>
  );
}

export function SeriesTile({ series }: { series: Series }) {
  return (
    <Link href={{ pathname: '/series/[slug]', params: { slug: series.slug } }} asChild>
      <Pressable accessibilityRole="button" style={styles.tile} accessibilityLabel={`${series.title}, ${series.sermonCount} sermons`}>
        <Artwork uri={series.artworkUrl} title={series.title} size={140} />
        <AppText style={text.callout} numberOfLines={2}>
          {series.title}
        </AppText>
        <AppText tone="tertiary" style={text.caption}>
          {series.sermonCount} {series.sermonCount === 1 ? 'sermon' : 'sermons'}
        </AppText>
      </Pressable>
    </Link>
  );
}

/**
 * Just enough Markdown for sermon notes: headings, bullet and numbered points, block quotes
 * (shown as scripture) and paragraphs. Keeps the app free of a heavy renderer.
 */
export function Notes({ markdown }: { markdown: string }) {
  const { palette } = useTheme();
  const blocks = markdown.replace(/\r\n/g, '\n').split(/\n{2,}/);
  return (
    <View style={styles.notes}>
      {blocks.map((block, i) => {
        const lines = block.split('\n').filter((l) => l.trim().length > 0);
        const first = lines[0] ?? '';
        if (/^#{1,3}\s/.test(first)) {
          return (
            <AppText key={i} style={text.title}>
              {strip(first.replace(/^#{1,3}\s/, ''))}
            </AppText>
          );
        }

        if (lines.every((l) => /^\s*([-*]|\d+\.)\s/.test(l))) {
          return (
            <View key={i} style={styles.list}>
              {lines.map((l, j) => (
                <View key={j} style={styles.bullet}>
                  <AppText tone="interactive">{/^\s*\d+\./.test(l) ? `${j + 1}.` : '•'}</AppText>
                  <AppText style={styles.flex}>{strip(l.replace(/^\s*([-*]|\d+\.)\s/, ''))}</AppText>
                </View>
              ))}
            </View>
          );
        }

        if (lines.every((l) => l.startsWith('>'))) {
          return (
            <View key={i} style={[styles.quote, { borderColor: palette.color.interactive }]}>
              <AppText style={[serif, styles.quoteText]}>{strip(lines.map((l) => l.replace(/^>\s?/, '')).join(' '))}</AppText>
            </View>
          );
        }

        return <AppText key={i}>{strip(lines.join(' '))}</AppText>;
      })}
    </View>
  );
}

/** Drops inline Markdown markers (bold, italics, links) we don't style separately. */
function strip(value: string): string {
  return value.replace(/\*\*(.+?)\*\*/g, '$1').replace(/[*_](.+?)[*_]/g, '$1').replace(/\[(.+?)\]\((.+?)\)/g, '$1');
}

const styles = StyleSheet.create({
  art: { borderRadius: radius.md, overflow: 'hidden', alignItems: 'center', justifyContent: 'center' },
  row: { flexDirection: 'row', gap: 12, alignItems: 'center', padding: space.sm },
  flex: { flex: 1, gap: 2 },
  track: { height: 4, borderRadius: 4, overflow: 'hidden', marginTop: 8 },
  progress: { height: '100%', borderRadius: 4 },
  tile: { width: 140, gap: 6 },
  notes: { gap: space.md },
  list: { gap: 6 },
  bullet: { flexDirection: 'row', gap: 8 },
  quote: { borderLeftWidth: 3, paddingLeft: space.md },
  quoteText: { fontSize: 18, lineHeight: 26 },
});
