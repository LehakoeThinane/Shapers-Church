import { radius, space } from '@shapers/tokens';
import { router } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, ScrollView, StyleSheet, View } from 'react-native';

import { Glass, PrimaryButton } from './glass';
import { AppText, Field } from './text';
import { errorMessage } from '@/lib/api';
import { blockedMessage, useChatNotice, useLiveChat, type ChatMessage } from '@/lib/chat';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

const MAX_LENGTH = 300;

/**
 * The chat under the livestream. Everyone can read; signed-in adults can post. Long-press a message to report it.
 * Before someone's first message we remind them the room is public and point personal things to the prayer form.
 */
export function LiveChat({ livestreamId, onPrayer }: { livestreamId: string; onPrayer: () => void }) {
  const { palette } = useTheme();
  const { room, send, report } = useLiveChat(livestreamId);
  const notice = useChatNotice();
  const [draft, setDraft] = useState('');
  const list = useRef<ScrollView>(null);
  const data = room.data;
  const count = data?.messages.length ?? 0;

  useEffect(() => {
    list.current?.scrollToEnd({ animated: true });
  }, [count]);

  if (room.isPending) return <ActivityIndicator />;
  if (!data) {
    return <AppText tone="tertiary">The chat isn't available right now.</AppText>;
  }

  if (!data.rules.open) {
    return <AppText tone="tertiary">The chat opens 15 minutes before the service.</AppText>;
  }

  const confirmReport = (message: ChatMessage) => {
    if (message.mine || data.me.blocked === 'SignIn') return;
    Alert.alert('Report this message?', 'A moderator will look at it. The author is not told who reported it.', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Report',
        style: 'destructive',
        onPress: () =>
          report.mutate(message.id, {
            onSuccess: () => Alert.alert('Thank you', 'The moderators have been told.'),
            onError: (e) => Alert.alert('Could not report', errorMessage(e)),
          }),
      },
    ]);
  };

  const submit = () => {
    const body = draft.trim();
    if (!body) return;
    send.mutate(body, { onSuccess: () => setDraft('') });
  };

  const blocked = blockedMessage(data.me.blocked);

  return (
    <Glass style={styles.card}>
      <ScrollView ref={list} style={styles.messages} contentContainerStyle={styles.messagesContent} nestedScrollEnabled>
        {data.messages.length === 0 && <AppText tone="tertiary">No messages yet. Say hello!</AppText>}
        {data.messages.map((m) => (
          <Pressable
            key={m.id}
            onLongPress={() => confirmReport(m)}
            accessibilityHint={m.mine ? undefined : 'Long-press to report this message'}
            accessibilityActions={m.mine ? undefined : [{ name: 'report', label: 'Report message' }]}
            onAccessibilityAction={() => confirmReport(m)}
            style={styles.message}>
            <View style={styles.authorRow}>
              <AppText tone={m.fromTeam ? 'accent' : 'secondary'} style={text.label}>
                {m.author}
              </AppText>
              {m.fromTeam && (
                <View style={[styles.badge, { borderColor: palette.color.accent }]}>
                  <AppText tone="accent" style={styles.badgeText}>
                    Team
                  </AppText>
                </View>
              )}
            </View>
            <AppText style={[text.callout, m.pending && styles.pending]}>{m.text}</AppText>
            {m.pending && (
              <AppText tone="tertiary" style={text.caption}>
                Waiting for a moderator. Only you can see this for now.
              </AppText>
            )}
          </Pressable>
        ))}
      </ScrollView>

      {blocked ? (
        <View style={styles.blocked}>
          <AppText tone="secondary" style={text.callout}>
            {blocked}
          </AppText>
          {data.me.blocked === 'SignIn' && <PrimaryButton label="Sign in" onPress={() => router.push('/sign-in')} />}
        </View>
      ) : notice.seen === false ? (
        <View style={styles.blocked}>
          <AppText style={text.headline}>This chat is public</AppText>
          <AppText tone="secondary" style={text.callout}>
            Everyone watching can read what you write, with your first name and initial. For anything personal, use the prayer form: only the pastoral team
            sees it.
          </AppText>
          <PrimaryButton label="Got it" onPress={notice.accept} />
        </View>
      ) : (
        <View style={styles.compose}>
          <Field
            placeholder="Say something kind"
            value={draft}
            onChangeText={setDraft}
            maxLength={MAX_LENGTH}
            multiline
            style={styles.input}
            accessibilityLabel="Chat message"
          />
          {send.error && (
            <AppText tone="danger" style={text.caption}>
              {errorMessage(send.error)}
            </AppText>
          )}
          <View style={styles.actions}>
            <Pressable accessibilityRole="button" onPress={onPrayer} style={[styles.pray, { borderColor: palette.color.glass.edge }]}>
              <AppText tone="interactive" style={text.callout}>
                Pray for me
              </AppText>
            </Pressable>
            <View style={styles.flex}>
              <PrimaryButton label="Send" onPress={submit} busy={send.isPending} disabled={!draft.trim()} />
            </View>
          </View>
          {data.rules.approvalRequired && !data.me.isModerator && (
            <AppText tone="tertiary" style={text.caption}>
              A moderator approves each message before it appears.
            </AppText>
          )}
        </View>
      )}
    </Glass>
  );
}

const styles = StyleSheet.create({
  card: { padding: space.md, gap: space.sm },
  messages: { height: 340 },
  messagesContent: { gap: space.sm, paddingBottom: space.xs },
  message: { gap: 2 },
  authorRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  badge: { borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: 6 },
  badgeText: { fontSize: 11, lineHeight: 15 },
  pending: { opacity: 0.6 },
  blocked: { gap: space.sm },
  compose: { gap: space.xs },
  input: { minHeight: 48, maxHeight: 110, paddingTop: 12, textAlignVertical: 'top' },
  actions: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  pray: { borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: space.md, paddingVertical: 12 },
  flex: { flex: 1 },
});
