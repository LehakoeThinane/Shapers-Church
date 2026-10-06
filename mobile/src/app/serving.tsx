import { radius, space } from '@shapers/tokens';
import { router, Stack } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';

import { Glass, PrimaryButton } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText, Field } from '@/components/text';
import { errorMessage, useSession } from '@/lib/api';
import { hhmm, servingDay, useAddBlockout, useAnswer, useBlockouts, useDeleteBlockout, useMySchedule, type MyAssignment } from '@/lib/serving';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

type Panel = 'Schedule' | 'Away';

export default function ServingScreen() {
  const { palette } = useTheme();
  const status = useSession((s) => s.status);
  const [panel, setPanel] = useState<Panel>('Schedule');

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Serving', headerShown: true, headerTransparent: true, headerTintColor: palette.color.interactive }} />
      <View style={styles.spacer} />
      {status !== 'signedIn' ? (
        <Glass style={styles.card}>
          <AppText style={text.headline}>Serving at Shapers</AppText>
          <AppText tone="secondary">Sign in to see when you're serving and answer requests.</AppText>
          <PrimaryButton label="Sign in" onPress={() => router.push('/sign-in')} />
        </Glass>
      ) : (
        <>
          <Glass cornerRadius={radius.pill} style={styles.switcher}>
            {(['Schedule', 'Away'] as Panel[]).map((p) => (
              <Pressable
                key={p}
                accessibilityRole="tab"
                accessibilityState={{ selected: panel === p }}
                onPress={() => setPanel(p)}
                style={[styles.segment, panel === p && { backgroundColor: palette.color.glass.fillActive }]}>
                <AppText tone={panel === p ? 'primary' : 'secondary'} style={text.callout}>
                  {p === 'Schedule' ? 'My schedule' : 'Days away'}
                </AppText>
              </Pressable>
            ))}
          </Glass>
          {panel === 'Schedule' ? <Schedule /> : <Away />}
        </>
      )}
    </Screen>
  );
}

function Schedule() {
  const schedule = useMySchedule();
  if (schedule.isPending) return <ActivityIndicator />;
  if (schedule.isError) return <AppText tone="secondary">We couldn't load your schedule. Check your connection.</AppText>;
  if (schedule.data.length === 0) {
    return (
      <Glass style={styles.card}>
        <AppText tone="secondary">You're not scheduled to serve at the moment. When a team leader asks you, it appears here.</AppText>
      </Glass>
    );
  }
  return (
    <View style={styles.list}>
      {schedule.data.map((a) => (
        <AssignmentCard key={a.id} assignment={a} />
      ))}
    </View>
  );
}

function AssignmentCard({ assignment: a }: { assignment: MyAssignment }) {
  const answer = useAnswer();
  const [declining, setDeclining] = useState(false);
  const [reason, setReason] = useState('');

  return (
    <Glass style={styles.card}>
      <View style={styles.row}>
        <AppText tone="accent" style={[text.label, styles.flex]}>
          {servingDay(a.date)}
        </AppText>
        <AppText tone={a.status === 'Accepted' ? 'accent' : a.status === 'Declined' ? 'danger' : 'secondary'} style={text.caption}>
          {a.status === 'Accepted' ? 'Serving' : a.status === 'Declined' ? "Can't make it" : 'Waiting for your answer'}
        </AppText>
      </View>
      <AppText style={text.headline}>
        {a.position} · {a.team}
      </AppText>
      <AppText tone="secondary">
        {a.planTitle}, {hhmm(a.startTime)}
      </AppText>

      {a.status !== 'Accepted' && !declining && (
        <PrimaryButton label="Yes, I can serve" busy={answer.isPending && answer.variables?.accept} onPress={() => answer.mutate({ id: a.id, accept: true })} />
      )}
      {a.status !== 'Declined' &&
        (declining ? (
          <View style={styles.list}>
            <Field value={reason} onChangeText={setReason} maxLength={300} placeholder="A short reason helps the team (optional)" accessibilityLabel="Why you can't serve" />
            <View style={styles.row}>
              <Pressable accessibilityRole="button" onPress={() => setDeclining(false)} style={styles.flex}>
                <AppText tone="secondary">Cancel</AppText>
              </Pressable>
              <Pressable accessibilityRole="button" onPress={() => answer.mutate({ id: a.id, accept: false, reason }, { onSuccess: () => setDeclining(false) })}>
                <AppText tone="danger">I can't serve</AppText>
              </Pressable>
            </View>
          </View>
        ) : (
          <Pressable accessibilityRole="button" onPress={() => setDeclining(true)}>
            <AppText tone="interactive" style={text.callout}>
              {a.status === 'Accepted' ? "Something came up? Let the team know you can't" : "I can't this time"}
            </AppText>
          </Pressable>
        ))}
      {a.status !== 'Declined' && (
        <Pressable accessibilityRole="button" onPress={() => router.push({ pathname: '/rehearse/[id]', params: { id: a.planId } })}>
          <AppText tone="interactive" style={text.callout}>
            See the plan and rehearse
          </AppText>
        </Pressable>
      )}
      {answer.error && <AppText tone="danger">{errorMessage(answer.error)}</AppText>}
    </Glass>
  );
}

/** Away on a coming Sunday: tap to mark it, tap again to undo. Team leaders won't ask you for those days. */
function Away() {
  const { palette } = useTheme();
  const blockouts = useBlockouts();
  const add = useAddBlockout();
  const remove = useDeleteBlockout();
  const sundays = comingSundays(12);

  if (blockouts.isPending) return <ActivityIndicator />;
  const awayOn = (date: string) => blockouts.data?.find((b) => b.from <= date && b.to >= date);

  return (
    <Glass style={styles.card}>
      <AppText style={text.headline}>Sundays you're away</AppText>
      <AppText tone="secondary">Tap a Sunday you can't serve. Team leaders won't ask you for those days.</AppText>
      <View style={styles.chips}>
        {sundays.map((date) => {
          const blockout = awayOn(date);
          return (
            <Pressable
              key={date}
              accessibilityRole="button"
              accessibilityState={{ selected: !!blockout }}
              accessibilityLabel={`${servingDay(date)}${blockout ? ', away' : ''}`}
              disabled={add.isPending || remove.isPending}
              onPress={() => (blockout ? remove.mutate(blockout.id) : add.mutate({ from: date, to: date, reason: null }))}
              style={[styles.chip, { borderColor: palette.color.glass.edge }, blockout && { backgroundColor: palette.color.accent, borderColor: palette.color.accent }]}>
              <AppText style={[text.callout, blockout && { color: palette.color.text.onAccent }]}>
                {shortDay(date)}
              </AppText>
            </Pressable>
          );
        })}
      </View>
      {blockouts.data && blockouts.data.some((b) => b.from !== b.to) && (
        <AppText tone="tertiary" style={text.caption}>
          Longer times away: {blockouts.data.filter((b) => b.from !== b.to).map((b) => `${shortDay(b.from)} to ${shortDay(b.to)}`).join(', ')}
        </AppText>
      )}
      {(add.error ?? remove.error) && <AppText tone="danger">{errorMessage(add.error ?? remove.error)}</AppText>}
    </Glass>
  );
}

function comingSundays(count: number): string[] {
  const d = new Date(Date.now() + 2 * 3600_000);
  d.setUTCDate(d.getUTCDate() + ((7 - d.getUTCDay()) % 7));
  return Array.from({ length: count }, (_, i) => new Date(d.getTime() + i * 7 * 86_400_000).toISOString().slice(0, 10));
}

const shortDay = (date: string) => new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'short' }).format(new Date(`${date}T12:00:00`));

const styles = StyleSheet.create({
  spacer: { height: space.xxl },
  switcher: { flexDirection: 'row', padding: 4 },
  segment: { flex: 1, alignItems: 'center', paddingVertical: 8, borderRadius: radius.pill },
  list: { gap: space.sm },
  card: { padding: space.lg, gap: space.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  chip: { paddingHorizontal: 14, paddingVertical: 8, borderRadius: radius.pill, borderWidth: 1 },
});
