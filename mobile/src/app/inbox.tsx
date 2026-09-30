import { space } from '@shapers/tokens';
import { router, Stack } from 'expo-router';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';

import { Glass } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText } from '@/components/text';
import { useInbox, useMarkRead, type InboxItem } from '@/lib/notifications';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

const whenFormat = new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

/** Every notification the member was sent, including ones their phone didn't show. */
export default function InboxScreen() {
  const { palette } = useTheme();
  const inbox = useInbox();
  const markRead = useMarkRead();
  const items = inbox.data?.items ?? [];

  const open = (item: InboxItem) => {
    if (!item.read) markRead.mutate(item.id);
    if (item.link) router.push(item.link as never);
  };

  return (
    <Screen>
      <Stack.Screen
        options={{
          title: 'Notifications',
          headerShown: true,
          headerTransparent: true,
          headerTintColor: palette.color.interactive,
          headerRight: () =>
            (inbox.data?.unread ?? 0) > 0 ? (
              <Pressable accessibilityRole="button" onPress={() => markRead.mutate(null)} hitSlop={8}>
                <AppText tone="interactive" style={text.callout}>
                  Mark all read
                </AppText>
              </Pressable>
            ) : null,
        }}
      />
      <View style={styles.spacer} />
      {inbox.isPending ? (
        <ActivityIndicator />
      ) : inbox.isError ? (
        <AppText tone="secondary">We couldn't load your notifications. Check your connection.</AppText>
      ) : items.length === 0 ? (
        <Glass style={styles.card}>
          <AppText tone="secondary">Nothing yet. We'll let you know when a service goes live, a sermon is published, and more.</AppText>
        </Glass>
      ) : (
        items.map((item) => (
          <Pressable key={item.id} accessibilityRole="button" accessibilityLabel={`${item.read ? '' : 'Unread. '}${item.title}. ${item.body}`} onPress={() => open(item)}>
            <Glass style={styles.card}>
              <View style={styles.row}>
                {!item.read && <View style={[styles.dot, { backgroundColor: palette.color.accent }]} />}
                <AppText style={[text.headline, styles.flex]}>{item.title}</AppText>
                <AppText tone="tertiary" style={text.caption}>
                  {whenFormat.format(new Date(item.createdAt))}
                </AppText>
              </View>
              <AppText tone="secondary">{item.body}</AppText>
            </Glass>
          </Pressable>
        ))
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  spacer: { height: space.xxl },
  card: { padding: space.lg, gap: space.xs },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
  dot: { width: 8, height: 8, borderRadius: 4 },
});
