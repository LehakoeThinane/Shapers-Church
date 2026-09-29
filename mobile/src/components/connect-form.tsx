import { radius, space } from '@shapers/tokens';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { Pressable, StyleSheet, Switch, View } from 'react-native';

import { Glass, PrimaryButton } from './glass';
import { AppText, Field } from './text';
import { api, errorMessage, unwrap, useSession, type Schemas } from '@/lib/api';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

type Reason = Schemas['ConnectReason'];

const labels: Record<Reason, string> = {
  FirstTime: "I'm new here",
  Decision: 'I made a decision to follow Jesus',
  Prayer: 'Please pray for me',
  MoreInfo: "I'd like to know more",
  JoinGroup: "I'd like to join a group",
  Serve: "I'd like to serve",
};

/**
 * A connect card. Signed-in members just choose and send; guests add their name and a way to reach them,
 * and agree to the church keeping those details.
 */
export function ConnectForm({
  reasons,
  initial = [],
  messageLabel,
  source,
  sourceId,
}: {
  reasons: Reason[];
  initial?: Reason[];
  messageLabel: string;
  source: string;
  sourceId?: string;
}) {
  const { palette } = useTheme();
  const signedIn = useSession((s) => s.status) === 'signedIn';
  const [chosen, setChosen] = useState<Reason[]>(initial);
  const [message, setMessage] = useState('');
  const [guest, setGuest] = useState({ firstName: '', lastName: '', mobile: '', consent: false });

  const send = useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/connect', {
          body: {
            firstName: signedIn ? null : guest.firstName,
            lastName: signedIn ? null : guest.lastName,
            mobile: signedIn ? null : guest.mobile,
            email: null,
            reasons: chosen,
            message: message || null,
            source,
            sourceId: sourceId ?? null,
            consentToKeepDetails: signedIn || guest.consent,
            policyVersion: '2026-09',
          },
        }),
      ),
  });

  if (send.isSuccess) {
    return (
      <Glass style={styles.card}>
        <AppText style={text.headline}>Thank you. We've got it.</AppText>
        <AppText tone="secondary">Someone from the church will be in touch this week.</AppText>
      </Glass>
    );
  }

  const guestReady = signedIn || (guest.firstName.trim() && guest.lastName.trim() && guest.mobile.trim().length >= 9 && guest.consent);

  return (
    <Glass style={styles.card}>
      {reasons.length > 1 &&
        reasons.map((r) => {
          const on = chosen.includes(r);
          return (
            <Pressable
              key={r}
              accessibilityRole="checkbox"
              accessibilityState={{ checked: on }}
              onPress={() => setChosen(on ? chosen.filter((x) => x !== r) : [...chosen, r])}
              style={[styles.option, { borderColor: on ? palette.color.interactive : palette.color.glass.edge }]}>
              <AppText style={text.callout}>{labels[r]}</AppText>
            </Pressable>
          );
        })}

      <Field placeholder={messageLabel} value={message} onChangeText={setMessage} multiline style={styles.message} maxLength={2000} />

      {!signedIn && (
        <View style={styles.guest}>
          <Field placeholder="First name" value={guest.firstName} onChangeText={(v) => setGuest({ ...guest, firstName: v })} autoComplete="given-name" />
          <Field placeholder="Last name" value={guest.lastName} onChangeText={(v) => setGuest({ ...guest, lastName: v })} autoComplete="family-name" />
          <Field placeholder="Mobile number" value={guest.mobile} onChangeText={(v) => setGuest({ ...guest, mobile: v })} keyboardType="phone-pad" autoComplete="tel" />
          <View style={styles.consent}>
            <AppText tone="secondary" style={[text.caption, styles.flex]}>
              Shapers Church may keep these details to contact me. (Protected under POPIA.)
            </AppText>
            <Switch value={guest.consent} onValueChange={(v) => setGuest({ ...guest, consent: v })} trackColor={{ true: palette.color.accent }} />
          </View>
        </View>
      )}

      {send.error && (
        <AppText tone="danger" style={text.callout}>
          {errorMessage(send.error)}
        </AppText>
      )}
      <PrimaryButton label="Send" onPress={() => send.mutate()} busy={send.isPending} disabled={chosen.length === 0 || !guestReady} />
    </Glass>
  );
}

const styles = StyleSheet.create({
  card: { padding: space.lg, gap: space.sm },
  option: { borderWidth: 1, borderRadius: radius.md, paddingHorizontal: space.md, paddingVertical: 12 },
  message: { height: 96, paddingTop: 12, textAlignVertical: 'top' },
  guest: { gap: space.sm },
  consent: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
});
