import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';

import { AuthScreen } from '@/components/auth-screen';
import { PrimaryButton } from '@/components/glass';
import { AppText, Field } from '@/components/text';
import { api, deviceName, errorMessage, startSession, unwrap } from '@/lib/api';
import { text } from '@/theme/type';

export default function VerifyScreen() {
  const { challengeId, maskedPhone } = useLocalSearchParams<{ challengeId: string; maskedPhone: string }>();
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const verify = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = unwrap(await api.POST('/api/auth/otp/verify', { body: { challengeId, code, device: deviceName } }));
      if (result.status === 'SignedIn' && result.tokens) {
        await startSession(result.tokens);
        router.dismissAll();
        return;
      }

      router.push({
        pathname: '/register',
        params: { challengeId, ticket: result.registrationTicket ?? '', existing: result.existingRecordFound ? '1' : '0' },
      });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthScreen title="Enter your code" subtitle={`We sent it to ${maskedPhone ?? 'your phone'}. It expires in 5 minutes.`}>
      <Field
        value={code}
        onChangeText={(v) => setCode(v.replace(/\D/g, '').slice(0, 6))}
        placeholder="123456"
        keyboardType="number-pad"
        textContentType="oneTimeCode"
        autoComplete="sms-otp"
        autoFocus
        accessibilityLabel="Six-digit code"
      />
      {error && (
        <AppText tone="danger" style={text.callout}>
          {error}
        </AppText>
      )}
      <PrimaryButton label="Continue" onPress={verify} busy={busy} disabled={code.length !== 6} />
    </AuthScreen>
  );
}
