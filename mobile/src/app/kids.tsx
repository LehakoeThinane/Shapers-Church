import { space } from '@shapers/tokens';
import { router, Stack } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';

import { Glass, PrimaryButton } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText, Field, Icon } from '@/components/text';
import { errorMessage, useSession } from '@/lib/api';
import { parseDate, timeOf, useAddChild, useCheckInKids, useMyKids, useUpdateCareNotes, type MyChild } from '@/lib/kids';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

/** Parents: their children, checking them in, and the pickup code to collect them. */
export default function KidsScreen() {
  const { palette } = useTheme();
  const status = useSession((s) => s.status);
  const kids = useMyKids();

  return (
    <Screen>
      <Stack.Screen options={{ title: 'Kids', headerShown: true, headerTransparent: true, headerTintColor: palette.color.interactive }} />
      <View style={styles.spacer} />
      {status !== 'signedIn' ? (
        <Glass style={styles.card}>
          <AppText style={text.headline}>Kids church</AppText>
          <AppText tone="secondary">Sign in to add your children and check them in on Sunday. New here? The kids team will check them in at the desk.</AppText>
          <PrimaryButton label="Sign in" onPress={() => router.push('/sign-in')} />
        </Glass>
      ) : kids.isPending ? (
        <ActivityIndicator />
      ) : kids.isError ? (
        <AppText tone="secondary">We couldn't load your children. Check your connection and try again.</AppText>
      ) : (
        <>
          <PickupCode kids={kids.data} />
          {kids.data.length > 0 && <CheckIn kids={kids.data} />}
          {kids.data.map((child) => (
            <ChildCard key={child.personId} child={child} />
          ))}
          <AddChild first={kids.data.length === 0} />
        </>
      )}
    </Screen>
  );
}

/** While any child is in class: the code to show the kids team, big enough to read across a room. */
function PickupCode({ kids }: { kids: MyChild[] }) {
  const waiting = kids.filter((k) => k.today && !k.today.collectedAt);
  if (waiting.length === 0) return null;
  const codes = [...new Set(waiting.map((k) => k.today!.pickupCode))];

  return (
    <Glass style={styles.card}>
      <AppText tone="secondary" style={text.label}>
        Pickup code
      </AppText>
      {codes.map((code) => (
        <AppText key={code} tone="accent" style={styles.code} accessibilityLabel={`Pickup code ${code.split('').join(' ')}`}>
          {code}
        </AppText>
      ))}
      <AppText tone="secondary">
        Show this to the kids team to collect {waiting.map((k) => `${k.firstName} (${k.today!.className})`).join(', ')}.
      </AppText>
    </Glass>
  );
}

function CheckIn({ kids }: { kids: MyChild[] }) {
  const { palette } = useTheme();
  const checkIn = useCheckInKids();
  const ready = kids.filter((k) => !k.today && k.className);
  const [chosen, setChosen] = useState<string[]>([]);
  if (ready.length === 0) return null;
  const selected = chosen.filter((id) => ready.some((k) => k.personId === id));

  return (
    <Glass style={styles.card}>
      <AppText style={text.headline}>Check in</AppText>
      <AppText tone="secondary">Choose who's going to kids church today.</AppText>
      {ready.map((k) => {
        const on = selected.includes(k.personId);
        return (
          <Pressable
            key={k.personId}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: on }}
            onPress={() => setChosen(on ? selected.filter((id) => id !== k.personId) : [...selected, k.personId])}
            style={[styles.choice, { borderColor: on ? palette.color.accent : palette.color.glass.edge, backgroundColor: on ? palette.color.glass.fillActive : 'transparent' }]}>
            <View style={styles.flex}>
              <AppText style={text.callout}>{k.firstName}</AppText>
              <AppText tone="tertiary" style={text.caption}>
                {k.className}
              </AppText>
            </View>
            {on && <Icon name={{ ios: 'checkmark.circle.fill', android: 'check_circle' }} />}
          </Pressable>
        );
      })}
      <PrimaryButton
        label={selected.length > 1 ? `Check in ${selected.length} children` : 'Check in'}
        disabled={selected.length === 0}
        busy={checkIn.isPending}
        onPress={() => checkIn.mutate(selected, { onSuccess: () => setChosen([]) })}
      />
      {checkIn.error && <AppText tone="danger">{errorMessage(checkIn.error)}</AppText>}
    </Glass>
  );
}

function ChildCard({ child: c }: { child: MyChild }) {
  const [editing, setEditing] = useState(false);
  const [notes, setNotes] = useState({ allergies: c.careNotes?.allergies ?? '', medical: c.careNotes?.medical ?? '', other: c.careNotes?.other ?? '' });
  const save = useUpdateCareNotes();

  return (
    <Glass style={styles.card}>
      <View style={styles.row}>
        <View style={styles.flex}>
          <AppText style={text.headline}>{c.name}</AppText>
          <AppText tone="tertiary" style={text.caption}>
            {c.age !== null && c.age !== undefined ? `Age ${c.age} · ` : ''}
            {c.className ?? 'No class for this age yet: please speak to the kids team'}
          </AppText>
        </View>
        {c.today && (
          <AppText tone={c.today.collectedAt ? 'secondary' : 'accent'} style={text.caption}>
            {c.today.collectedAt ? `Collected ${timeOf(c.today.collectedAt)}` : `In class since ${timeOf(c.today.checkedInAt)}`}
          </AppText>
        )}
      </View>

      {editing ? (
        <View style={styles.list}>
          <Field value={notes.allergies} onChangeText={(v) => setNotes({ ...notes, allergies: v })} maxLength={500} placeholder="Allergies" accessibilityLabel="Allergies" />
          <Field value={notes.medical} onChangeText={(v) => setNotes({ ...notes, medical: v })} maxLength={500} placeholder="Medical needs" accessibilityLabel="Medical needs" />
          <Field value={notes.other} onChangeText={(v) => setNotes({ ...notes, other: v })} maxLength={500} placeholder="Anything else the team should know" accessibilityLabel="Anything else" />
          <PrimaryButton
            label="Save care notes"
            busy={save.isPending}
            onPress={() => save.mutate({ childId: c.personId, allergies: notes.allergies || null, medical: notes.medical || null, other: notes.other || null }, { onSuccess: () => setEditing(false) })}
          />
          {save.error && <AppText tone="danger">{errorMessage(save.error)}</AppText>}
        </View>
      ) : (
        <Pressable accessibilityRole="button" onPress={() => setEditing(true)}>
          <AppText tone="secondary">
            {c.careNotes ? [c.careNotes.allergies, c.careNotes.medical, c.careNotes.other].filter(Boolean).join(' · ') : 'No care notes.'}
          </AppText>
          <AppText tone="interactive" style={text.callout}>
            {c.careNotes ? 'Change care notes' : 'Add allergies or medical needs'}
          </AppText>
        </Pressable>
      )}
    </Glass>
  );
}

function AddChild({ first }: { first: boolean }) {
  const { palette } = useTheme();
  const add = useAddChild();
  const [open, setOpen] = useState(first);
  const [form, setForm] = useState({ firstName: '', lastName: '', dob: '', allergies: '', medical: '', other: '' });
  const [consent, setConsent] = useState(false);
  const dateOfBirth = parseDate(form.dob);

  if (!open) {
    return (
      <Pressable accessibilityRole="button" onPress={() => setOpen(true)}>
        <AppText tone="interactive" style={[text.callout, styles.centre]}>
          Add a child
        </AppText>
      </Pressable>
    );
  }

  return (
    <Glass style={styles.card}>
      <AppText style={text.headline}>{first ? 'Add your children' : 'Add a child'}</AppText>
      <AppText tone="secondary">Their date of birth decides their class. Care notes are only seen by kids leaders.</AppText>
      <Field value={form.firstName} onChangeText={(v) => setForm({ ...form, firstName: v })} placeholder="First name" accessibilityLabel="Child's first name" />
      <Field value={form.lastName} onChangeText={(v) => setForm({ ...form, lastName: v })} placeholder="Last name" accessibilityLabel="Child's last name" />
      <Field value={form.dob} onChangeText={(v) => setForm({ ...form, dob: v })} placeholder="Date of birth, e.g. 14/03/2019" accessibilityLabel="Date of birth" keyboardType="numbers-and-punctuation" />
      {form.dob.length >= 8 && !dateOfBirth && <AppText tone="danger">That date doesn't look right. Try day/month/year.</AppText>}
      <Field value={form.allergies} onChangeText={(v) => setForm({ ...form, allergies: v })} maxLength={500} placeholder="Allergies (optional)" accessibilityLabel="Allergies" />
      <Field value={form.medical} onChangeText={(v) => setForm({ ...form, medical: v })} maxLength={500} placeholder="Medical needs (optional)" accessibilityLabel="Medical needs" />
      <Field value={form.other} onChangeText={(v) => setForm({ ...form, other: v })} maxLength={500} placeholder="Anything else the team should know (optional)" accessibilityLabel="Anything else" />
      <Pressable accessibilityRole="checkbox" accessibilityState={{ checked: consent }} onPress={() => setConsent(!consent)} style={styles.row}>
        <View style={[styles.box, { borderColor: consent ? palette.color.accent : palette.color.text.tertiary, backgroundColor: consent ? palette.color.accent : 'transparent' }]} />
        <AppText tone="secondary" style={styles.flex}>
          I'm this child's parent or guardian, and I agree to Shapers Church keeping these details for kids church.
        </AppText>
      </Pressable>
      <PrimaryButton
        label="Add child"
        disabled={!consent || !form.firstName.trim() || !form.lastName.trim() || !dateOfBirth}
        busy={add.isPending}
        onPress={() =>
          add.mutate(
            {
              firstName: form.firstName,
              lastName: form.lastName,
              dateOfBirth: dateOfBirth!,
              allergies: form.allergies || null,
              medical: form.medical || null,
              other: form.other || null,
              guardianConsent: consent,
            },
            {
              onSuccess: () => {
                setForm({ firstName: '', lastName: form.lastName, dob: '', allergies: '', medical: '', other: '' });
                setConsent(false);
                setOpen(false);
              },
            },
          )
        }
      />
      {add.error && <AppText tone="danger">{errorMessage(add.error)}</AppText>}
    </Glass>
  );
}

const styles = StyleSheet.create({
  spacer: { height: space.xxl },
  card: { padding: space.lg, gap: space.sm },
  list: { gap: space.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
  centre: { textAlign: 'center', paddingVertical: space.sm },
  code: { fontSize: 56, lineHeight: 64, fontWeight: '700', letterSpacing: 8 },
  choice: { flexDirection: 'row', alignItems: 'center', gap: space.sm, padding: space.md, borderRadius: 16, borderWidth: 1 },
  box: { width: 22, height: 22, borderRadius: 6, borderWidth: 2 },
});
