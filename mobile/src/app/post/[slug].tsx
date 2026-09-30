import { space } from '@shapers/tokens';
import { Image } from 'expo-image';
import { Stack, useLocalSearchParams } from 'expo-router';
import { ActivityIndicator, Share, Pressable, StyleSheet, View } from 'react-native';

import { Screen } from '@/components/screen';
import { Notes } from '@/components/sermon-bits';
import { AppText, Icon } from '@/components/text';
import { usePost } from '@/lib/content';
import { useMediaSettings } from '@/lib/media';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

const dateFormat = new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'long', year: 'numeric' });

/** A news item or blog article. */
export default function PostScreen() {
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const { palette } = useTheme();
  const post = usePost(slug);
  const lowData = useMediaSettings((s) => s.lowData);
  const p = post.data;

  return (
    <Screen>
      <Stack.Screen options={{ title: '', headerTransparent: true, headerTintColor: palette.color.interactive }} />
      {!p ? (
        post.isError ? (
          <AppText tone="secondary">We couldn&apos;t load this. Check your connection.</AppText>
        ) : (
          <ActivityIndicator />
        )
      ) : (
        <>
          {p.post.coverImageUrl && !lowData && (
            <Image source={{ uri: p.post.coverImageUrl }} style={styles.cover} contentFit="cover" accessibilityIgnoresInvertColors />
          )}
          <View style={styles.header}>
            <AppText tone="accent" style={text.label}>
              {p.post.kind === 'News' ? 'News' : 'From the blog'}
            </AppText>
            <AppText style={[serif, styles.title]}>{p.post.title}</AppText>
            <View style={styles.meta}>
              <AppText tone="tertiary" style={[text.caption, styles.flex]}>
                {[p.post.author, dateFormat.format(new Date(p.post.publishedAt))].filter(Boolean).join(' · ')}
              </AppText>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Share"
                hitSlop={8}
                onPress={() => void Share.share({ message: `${p.post.title}\nhttps://shaperschurch.com/blog/${p.post.slug}` })}>
                <Icon name={{ ios: 'square.and.arrow.up', android: 'share' }} size={20} />
              </Pressable>
            </View>
          </View>
          <Notes markdown={p.body} />
        </>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  cover: { width: '100%', aspectRatio: 16 / 9, borderRadius: 22, marginTop: space.xl },
  header: { gap: space.xs, marginTop: space.xl },
  title: { fontSize: 28, lineHeight: 34 },
  meta: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
});
