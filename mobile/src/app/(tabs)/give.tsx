import { radius, space } from '@shapers/tokens';
import { useQueryClient } from '@tanstack/react-query';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';

import { Glass, PrimaryButton } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText, Field } from '@/components/text';
import { errorMessage, useSession } from '@/lib/api';
import { parseRands, rands, useGivingPage, useMyGiving, useStartGift } from '@/lib/giving';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

const quickAmounts = [100, 250, 500, 1000];

/** Give by card (on the payment provider's secure page) or by bank transfer, and see your own giving. */
export default function GiveScreen() {
  const { palette } = useTheme();
  const status = useSession((s) => s.status);
  const queryClient = useQueryClient();
  const page = useGivingPage();
  const start = useStartGift();
  const [fundId, setFundId] = useState<string | null>(null);
  const [amount, setAmount] = useState('');
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const value = parseRands(amount);
  const signedIn = status === 'signedIn';

  const give = async () => {
    const data = page.data;
    if (!data) return;
    if (!data.cardGivingEnabled) {
      if (data.cardLinkUrl) await WebBrowser.openBrowserAsync(data.cardLinkUrl);
      return;
    }

    const chosen = fundId ?? data.funds[0]?.id;
    if (!chosen || value === null) return;
    start.mutate(
      { fundId: chosen, amount: value, name: signedIn ? null : name, email: signedIn ? null : email },
      {
        onSuccess: async (started) => {
          await WebBrowser.openBrowserAsync(started.redirectUrl);
          // Back from the payment page: the confirmation arrives within moments.
          void queryClient.invalidateQueries({ queryKey: ['giving', 'mine'] });
        },
      },
    );
  };

  if (page.isPending) {
    return (
      <Screen>
        <AppText style={text.largeTitle}>Give</AppText>
        <ActivityIndicator />
      </Screen>
    );
  }

  if (!page.data) {
    return (
      <Screen>
        <AppText style={text.largeTitle}>Give</AppText>
        <AppText tone="secondary">We couldn't load giving. Check your connection and try again.</AppText>
      </Screen>
    );
  }

  const data = page.data;
  const selected = fundId ?? data.funds[0]?.id;
  const canGive = data.cardGivingEnabled ? !!selected && value !== null && (signedIn || (name.trim() !== '' && email.includes('@'))) : !!data.cardLinkUrl;

  return (
    <Screen>
      <AppText style={text.largeTitle}>Give</AppText>
      <AppText tone="secondary">Thank you for giving to the work of Shapers Church.</AppText>

      <Glass style={styles.card}>
        <AppText style={text.headline}>Give by card</AppText>
        {data.cardGivingEnabled ? (
          <>
            <View style={styles.chips} accessibilityRole="radiogroup" accessibilityLabel="What your gift is for">
              {data.funds.map((f) => {
                const on = f.id === selected;
                return (
                  <Pressable
                    key={f.id}
                    accessibilityRole="radio"
                    accessibilityState={{ selected: on }}
                    onPress={() => setFundId(f.id)}
                    style={[styles.chip, { borderColor: on ? palette.color.accent : palette.color.glass.edge, backgroundColor: on ? palette.color.glass.fillActive : 'transparent' }]}>
                    <AppText tone={on ? 'primary' : 'secondary'} style={text.callout}>
                      {f.name}
                    </AppText>
                  </Pressable>
                );
              })}
            </View>
            <View style={styles.chips}>
              {quickAmounts.map((a) => (
                <Pressable
                  key={a}
                  accessibilityRole="button"
                  accessibilityLabel={`R${a}`}
                  onPress={() => setAmount(String(a))}
                  style={[styles.chip, { borderColor: value === a ? palette.color.accent : palette.color.glass.edge }]}>
                  <AppText style={text.callout}>R{a}</AppText>
                </Pressable>
              ))}
            </View>
            <Field value={amount} onChangeText={setAmount} keyboardType="decimal-pad" placeholder="Amount in rands" accessibilityLabel="Amount in rands" />
            {amount !== '' && value === null && <AppText tone="danger">Enter an amount from R5, e.g. 250 or 250.50.</AppText>}
            {!signedIn && (
              <>
                <Field value={name} onChangeText={setName} placeholder="Your name" accessibilityLabel="Your name" autoComplete="name" />
                <Field value={email} onChangeText={setEmail} placeholder="Email for your receipt" accessibilityLabel="Email for your receipt" keyboardType="email-address" autoCapitalize="none" autoComplete="email" />
              </>
            )}
            <PrimaryButton label={value !== null ? `Give ${rands(Math.round(value * 100))}` : 'Give'} disabled={!canGive} busy={start.isPending} onPress={() => void give()} />
            <AppText tone="tertiary" style={text.caption}>
              You'll pay on Yoco's secure page. Shapers Church never sees your card details.
            </AppText>
          </>
        ) : (
          <>
            <AppText tone="secondary">Pay securely by card through the church's Yoco page. Add a note to say whether it's your tithe or an offering.</AppText>
            <PrimaryButton label="Give by card" disabled={!canGive} onPress={() => void give()} />
          </>
        )}
        {start.error && <AppText tone="danger">{errorMessage(start.error)}</AppText>}
      </Glass>

      {data.eft.accountNumber && (
        <Glass style={styles.card}>
          <AppText style={text.headline}>Bank transfer</AppText>
          <AppText tone="tertiary" style={text.caption}>
            Press and hold to copy.
          </AppText>
          <Detail label="Bank" value={data.eft.bank} />
          <Detail label="Account name" value={data.eft.accountName} />
          <Detail label="Account number" value={data.eft.accountNumber} />
          <Detail label="Branch code" value={data.eft.branchCode} />
          <Detail label="Reference" value={data.eft.reference} />
        </Glass>
      )}

      {signedIn && <MyGiving />}
    </Screen>
  );
}

function Detail({ label, value }: { label: string; value: string | null }) {
  if (!value) return null;
  return (
    <View style={styles.detail}>
      <AppText tone="secondary" style={text.caption}>
        {label}
      </AppText>
      <AppText selectable style={text.callout}>
        {value}
      </AppText>
    </View>
  );
}

function MyGiving() {
  const mine = useMyGiving();
  if (mine.isPending) return <ActivityIndicator />;
  if (!mine.data) return null;
  const year = new Date().getFullYear().toString();
  const thisYear = mine.data.filter((g) => g.givenOn.startsWith(year));

  return (
    <Glass style={styles.card}>
      <AppText style={text.headline}>My giving</AppText>
      {mine.data.length === 0 ? (
        <AppText tone="secondary">Your gifts appear here once they've gone through, including bank transfers the church has recorded.</AppText>
      ) : (
        <>
          <AppText tone="secondary">
            {year} so far: {rands(thisYear.reduce((sum, g) => sum + g.amountCents, 0))}
          </AppText>
          {mine.data.slice(0, 12).map((g) => (
            <View key={g.id} style={styles.row}>
              <View style={styles.flex}>
                <AppText style={text.callout}>{g.fundName}</AppText>
                <AppText tone="tertiary" style={text.caption}>
                  {new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(`${g.givenOn}T12:00:00`))}
                  {g.method === 'Card' ? ' · Card' : g.method === 'Eft' ? ' · EFT' : ' · Cash'}
                </AppText>
              </View>
              <AppText style={text.callout}>{rands(g.amountCents)}</AppText>
            </View>
          ))}
        </>
      )}
    </Glass>
  );
}

const styles = StyleSheet.create({
  card: { padding: space.lg, gap: space.sm },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: space.sm },
  chip: { paddingHorizontal: 14, paddingVertical: 8, borderRadius: radius.pill, borderWidth: 1 },
  detail: { gap: 2 },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  flex: { flex: 1 },
});
