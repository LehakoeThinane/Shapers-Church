import { router } from 'expo-router';
import { useState } from 'react';

import { AuthScreen } from '@/components/auth-screen';
import { PrimaryButton } from '@/components/glass';
import { AppText, Field } from '@/components/text';
import { api, errorMessage, unwrap } from '@/lib/api';
import { text } from '@/theme/type';

export default function SignInScreen() {
  const [phone, setPhone] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const requestCode = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = unwrap(await api.POST('/api/auth/otp/request', { body: { phone } }));
      router.push({ pathname: '/verify', params: { challengeId: result.challengeId, maskedPhone: result.maskedPhone } });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthScreen title="Sign in" subtitle="We'll send a 6-digit code to your phone. No password needed.">
      <Field
        value={phone}
        onChangeText={setPhone}
        placeholder="082 123 4567"
        keyboardType="phone-pad"
        textContentType="telephoneNumber"
        autoComplete="tel"
        autoFocus
        accessibilityLabel="Mobile number"
      />
      {error && (
        <AppText tone="danger" style={text.callout}>
          {error}
        </AppText>
      )}
      <PrimaryButton label="Send code" onPress={requestCode} busy={busy} disabled={phone.trim().length < 9} />
      <AppText tone="tertiary" style={text.caption}>
        Standard SMS rates may apply.
      </AppText>
    </AuthScreen>
  );
}
