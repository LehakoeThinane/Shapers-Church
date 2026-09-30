import { radius, space } from '@shapers/tokens';
import { router, Stack } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, Alert, Pressable, StyleSheet, Switch, View } from 'react-native';

import { Glass, PrimaryButton } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText, Field, Icon } from '@/components/text';
import { errorMessage, useSession } from '@/lib/api';
import {
  prayerStatusLabel,
  useMarkAnswered,
  useMyPrayers,
  usePrayed,
  usePrayerWall,
  useSubmitPrayer,
  useWithdrawPrayer,
  type MyPrayer,
  type WallItem,
} from '@/lib/prayer';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

type Panel = 'Wall' | 'Ask' | 'Mine';
const panels: { key: Panel; label: string }[] = [
  { key: 'Wall', label: 'Prayer wall' },
  { key: 'Ask', label: 'Ask for prayer' },
  { key: 'Mine', label: 'My requests' },
];

const dayFormat = new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'short' });

export default function PrayerScreen() {
  const { palette } = useTheme();
  const status = useSession((s) => s.status);
  const [panel, setPanel] = useState<Panel>('Wall');

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Prayer', headerShown: true, headerTransparent: true, headerTintColor: palette.color.interactive }} />
      <View style={styles.spacer} />

      {status !== 'signedIn' ? (
        <Glass style={styles.card}>
          <AppText style={text.headline}>Pray with the church family</AppText>
          <AppText tone="secondary">Sign in to see the prayer wall and share a request.</AppText>
          <PrimaryButton label="Sign in" onPress={() => router.push('/sign-in')} />
        </Glass>
      ) : (
        <>
          <Glass cornerRadius={radius.pill} style={styles.switcher}>
            {panels.map((p) => (
              <Pressable
                key={p.key}
                accessibilityRole="tab"
                accessibilityState={{ selected: panel === p.key }}
                onPress={() => setPanel(p.key)}
                style={[styles.segment, panel === p.key && { backgroundColor: palette.color.glass.fillActive }]}>
                <AppText tone={panel === p.key ? 'primary' : 'secondary'} style={text.callout}>
                  {p.label}
                </AppText>
              </Pressable>
            ))}
          </Glass>
          {panel === 'Wall' && <Wall onAsk={() => setPanel('Ask')} />}
          {panel === 'Ask' && <AskForm onSent={() => setPanel('Mine')} />}
          {panel === 'Mine' && <Mine />}
        </>
      )}
    </Screen>
  );
}

function Wall({ onAsk }: { onAsk: () => void }) {
  const wall = usePrayerWall();
  if (wall.isPending) return <ActivityIndicator />;
  if (wall.isError) return <AppText tone="secondary">We couldn't load the wall. Check your connection.</AppText>;
  if (wall.data.length === 0) {
    return (
      <Glass style={styles.card}>
        <AppText tone="secondary">No requests on the wall right now.</AppText>
        <Pressable accessibilityRole="button" onPress={onAsk}>
          <AppText tone="interactive">Ask for prayer</AppText>
        </Pressable>
      </Glass>
    );
  }
  return (
    <View style={styles.list}>
      {wall.data.map((w) => (
        <WallCard key={w.id} item={w} />
      ))}
    </View>
  );
}

function WallCard({ item }: { item: WallItem }) {
  const prayed = usePrayed();
  const done = item.iPrayed;
  return (
    <Glass style={styles.card}>
      <View style={styles.row}>
        <AppText style={[text.headline, styles.flex]}>{item.name}</AppText>
        <AppText tone="tertiary" style={text.caption}>
          {dayFormat.format(new Date(item.sharedAt))}
        </AppText>
      </View>
      <AppText>{item.text}</AppText>
      {item.answered && (
        <AppText tone="accent" style={text.callout}>
          Answered prayer
        </AppText>
      )}
      <View style={styles.row}>
        <AppText tone="tertiary" style={[text.caption, styles.flex]}>
          {item.prayedCount === 0 ? 'Be the first to pray' : `${item.prayedCount} ${item.prayedCount === 1 ? 'person has' : 'people have'} prayed`}
        </AppText>
        {!item.isMine && (
          <Pressable
            accessibilityRole="button"
            accessibilityState={{ disabled: done }}
            accessibilityLabel={done ? 'You prayed for this' : `I prayed for ${item.name}`}
            disabled={done}
            onPress={() => prayed.mutate(item.id)}
            style={styles.prayed}>
            <Icon name={done ? { ios: 'hands.sparkles.fill', android: 'volunteer_activism' } : { ios: 'hands.sparkles', android: 'self_improvement' }} size={18} />
            <AppText tone="interactive" style={text.callout}>
              {done ? 'Prayed' : 'I prayed'}
            </AppText>
          </Pressable>
        )}
      </View>
    </Glass>
  );
}

function AskForm({ onSent }: { onSent: () => void }) {
  const submit = useSubmitPrayer();
  const [request, setRequest] = useState('');
  const [shareOnWall, setShareOnWall] = useState(true);
  const [anonymous, setAnonymous] = useState(false);
  const [consent, setConsent] = useState(false);

  return (
    <Glass style={styles.card}>
      <AppText style={text.headline}>How can we pray for you?</AppText>
      <Field
        multiline
        value={request}
        onChangeText={setRequest}
        maxLength={1000}
        placeholder="Your request"
        style={styles.textArea}
        accessibilityLabel="Your prayer request"
      />
      <Toggle
        label="Share on the prayer wall"
        hint={shareOnWall ? 'Members see it after our team reviews it. They may shorten it to protect others.' : 'Only the pastors will see it.'}
        value={shareOnWall}
        onChange={setShareOnWall}
      />
      {shareOnWall && <Toggle label="Hide my name" hint="The pastors still see who asked, so they can follow up." value={anonymous} onChange={setAnonymous} />}
      <Toggle
        label="The church may keep this request"
        hint="Prayer requests are personal. We use them only to pray for you and follow up."
        value={consent}
        onChange={setConsent}
      />
      {submit.error && <AppText tone="danger">{errorMessage(submit.error)}</AppText>}
      <PrimaryButton
        label="Send request"
        busy={submit.isPending}
        disabled={!request.trim() || !consent}
        onPress={() =>
          submit.mutate(
            { text: request, shareOnWall, anonymous: shareOnWall && anonymous, consent },
            {
              onSuccess: () => {
                setRequest('');
                onSent();
              },
            },
          )
        }
      />
    </Glass>
  );
}

function Mine() {
  const mine = useMyPrayers();
  if (mine.isPending) return <ActivityIndicator />;
  if (mine.isError) return <AppText tone="secondary">We couldn't load your requests. Check your connection.</AppText>;
  if (mine.data.length === 0) {
    return (
      <Glass style={styles.card}>
        <AppText tone="secondary">You haven't asked for prayer yet.</AppText>
      </Glass>
    );
  }
  return (
    <View style={styles.list}>
      {mine.data.map((p) => (
        <MyCard key={p.id} prayer={p} />
      ))}
    </View>
  );
}

function MyCard({ prayer: p }: { prayer: MyPrayer }) {
  const answer = useMarkAnswered();
  const withdraw = useWithdrawPrayer();
  const [note, setNote] = useState('');
  const [answering, setAnswering] = useState(false);
  const open = p.status !== 'Closed';

  return (
    <Glass style={styles.card}>
      <View style={styles.row}>
        <AppText tone="accent" style={[text.label, styles.flex]}>
          {prayerStatusLabel(p)}
        </AppText>
        <AppText tone="tertiary" style={text.caption}>
          {dayFormat.format(new Date(p.createdAt))}
        </AppText>
      </View>
      <AppText>{p.text}</AppText>
      {p.wallText && p.wallText !== p.text && (
        <AppText tone="tertiary" style={text.caption}>
          On the wall as: “{p.wallText}”
        </AppText>
      )}
      {p.reviewNote && (
        <AppText tone="secondary" style={text.callout}>
          {p.reviewNote}
        </AppText>
      )}
      {p.prayedCount > 0 && (
        <AppText tone="secondary" style={text.callout}>
          {p.prayedCount} {p.prayedCount === 1 ? 'person has' : 'people have'} prayed for you
        </AppText>
      )}
      {p.answeredAt ? (
        <AppText tone="accent" style={text.callout}>
          Answered{p.answerNote ? `: ${p.answerNote}` : ''}
        </AppText>
      ) : answering ? (
        <View style={styles.list}>
          <Field value={note} onChangeText={setNote} maxLength={1000} placeholder="What happened? (optional)" accessibilityLabel="How your prayer was answered" />
          <PrimaryButton label="Mark answered" busy={answer.isPending} onPress={() => answer.mutate({ id: p.id, note: note.trim() || null })} />
        </View>
      ) : (
        <Pressable accessibilityRole="button" onPress={() => setAnswering(true)}>
          <AppText tone="interactive" style={text.callout}>
            This prayer was answered
          </AppText>
        </Pressable>
      )}
      {open && (
        <Pressable
          accessibilityRole="button"
          disabled={withdraw.isPending}
          onPress={() =>
            Alert.alert('Withdraw this request?', 'It comes off the wall. The pastors keep it until it is deleted.', [
              { text: 'Keep it', style: 'cancel' },
              { text: 'Withdraw', style: 'destructive', onPress: () => withdraw.mutate(p.id) },
            ])
          }>
          <AppText tone="danger" style={text.callout}>
            Withdraw
          </AppText>
        </Pressable>
      )}
      {(answer.error ?? withdraw.error) && <AppText tone="danger">{errorMessage(answer.error ?? withdraw.error)}</AppText>}
    </Glass>
  );
}

function Toggle({ label, hint, value, onChange }: { label: string; hint: string; value: boolean; onChange: (value: boolean) => void }) {
  const { palette } = useTheme();
  return (
    <View style={styles.row}>
      <View style={styles.flex}>
        <AppText style={text.callout}>{label}</AppText>
        <AppText tone="tertiary" style={text.caption}>
          {hint}
        </AppText>
      </View>
      <Switch value={value} onValueChange={onChange} trackColor={{ true: palette.color.accent }} accessibilityLabel={label} />
    </View>
  );
}

const styles = StyleSheet.create({
  spacer: { height: space.xxl },
  switcher: { flexDirection: 'row', padding: 4 },
  segment: { flex: 1, alignItems: 'center', paddingVertical: 8, borderRadius: radius.pill },
  list: { gap: space.sm },
  card: { padding: space.lg, gap: space.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1, gap: 2 },
  prayed: { flexDirection: 'row', alignItems: 'center', gap: 6, paddingVertical: 4 },
  textArea: { height: 120, paddingTop: 14, textAlignVertical: 'top' },
});
