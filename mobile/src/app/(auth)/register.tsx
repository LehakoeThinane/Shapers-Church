import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, Switch, View } from 'react-native';

import { AuthScreen } from '@/components/auth-screen';
import { PrimaryButton } from '@/components/glass';
import { AppText, Field } from '@/components/text';
import { api, deviceName, errorMessage, startSession, unwrap } from '@/lib/api';
import { currentNoticeVersion } from '@/lib/privacy';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';


const optionalConsents = [
  { purpose: 'communications.whatsapp', label: 'WhatsApp messages from the church' },
  { purpose: 'communications.sms', label: 'SMS messages' },
  { purpose: 'communications.email', label: 'Email' },
  { purpose: 'communications.push', label: 'App notifications' },
] as const;

export default function RegisterScreen() {
  const { palette } = useTheme();
  const { challengeId, ticket, existing } = useLocalSearchParams<{ challengeId: string; ticket: string; existing: string }>();
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [keepRecord, setKeepRecord] = useState(false);
  const [channels, setChannels] = useState<Record<string, boolean>>({ 'communications.whatsapp': true, 'communications.push': true });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const register = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = unwrap(
        await api.POST('/api/auth/register', {
          body: {
            challengeId,
            registrationTicket: ticket,
            firstName,
            lastName,
            email: email || null,
            campusId: null,
            policyVersion: await currentNoticeVersion(),
            consents: [
              { purpose: 'processing.church_record', granted: keepRecord },
              ...optionalConsents.map((c) => ({ purpose: c.purpose, granted: !!channels[c.purpose] })),
            ],
            device: deviceName,
          },
        }),
      );
      if (result.tokens) {
        await startSession(result.tokens);
        router.dismissAll();
      }
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthScreen
      title="Welcome to Shapers"
      subtitle={existing === '1' ? "We found a church record with your number. Confirm your name and we'll link it to your account." : 'Tell us a little about you.'}>
      <Field value={firstName} onChangeText={setFirstName} placeholder="First name" textContentType="givenName" autoComplete="given-name" />
      <Field value={lastName} onChangeText={setLastName} placeholder="Last name" textContentType="familyName" autoComplete="family-name" />
      <Field value={email} onChangeText={setEmail} placeholder="Email (optional)" keyboardType="email-address" autoCapitalize="none" autoComplete="email" />

      <ConsentRow
        label="Keep my church record"
        detail="Shapers Church keeps your details to care for you as part of the church. Church membership is personal information protected by POPIA. Required to create an account."
        value={keepRecord}
        onChange={setKeepRecord}
        trackColor={palette.color.accent}
      />
      <Pressable accessibilityRole="link" onPress={() => router.push('/privacy-notice')}>
        <AppText tone="interactive" style={text.callout}>
          Read how we look after your information
        </AppText>
      </Pressable>
      <AppText tone="secondary" style={text.callout}>
        How may we contact you? You can change this any time.
      </AppText>
      {optionalConsents.map((c) => (
        <ConsentRow key={c.purpose} label={c.label} value={!!channels[c.purpose]} onChange={(v) => setChannels({ ...channels, [c.purpose]: v })} trackColor={palette.color.accent} />
      ))}

      {error && (
        <AppText tone="danger" style={text.callout}>
          {error}
        </AppText>
      )}
      <PrimaryButton label="Create account" onPress={register} busy={busy} disabled={!firstName.trim() || !lastName.trim() || !keepRecord} />
    </AuthScreen>
  );
}

function ConsentRow({ label, detail, value, onChange, trackColor }: { label: string; detail?: string; value: boolean; onChange: (v: boolean) => void; trackColor: string }) {
  return (
    <Pressable accessibilityRole="switch" accessibilityState={{ checked: value }} onPress={() => onChange(!value)} style={styles.row}>
      <View style={styles.flex}>
        <AppText style={text.headline}>{label}</AppText>
        {detail && (
          <AppText tone="tertiary" style={text.caption}>
            {detail}
          </AppText>
        )}
      </View>
      <Switch value={value} onValueChange={onChange} trackColor={{ true: trackColor }} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  flex: { flex: 1, gap: 2 },
});
