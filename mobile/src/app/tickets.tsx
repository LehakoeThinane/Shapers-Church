import { space } from '@shapers/tokens';
import { Link, router, Stack } from 'expo-router';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import QRCode from 'react-native-qrcode-svg';

import { Glass, PrimaryButton } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText } from '@/components/text';
import { useSession } from '@/lib/api';
import { eventWhen, ticketQrValue, useCancelRegistration, useMyRegistrations, type Registration } from '@/lib/events';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

/** Tickets for the door. Saved on the phone, so they open without signal. */
export default function TicketsScreen() {
  const { palette } = useTheme();
  const status = useSession((s) => s.status);
  const { upcoming, offline, isPending } = useMyRegistrations();

  return (
    <Screen>
      <Stack.Screen options={{ title: 'My tickets', headerShown: true, headerTransparent: true, headerTintColor: palette.color.interactive }} />
      <View style={styles.spacer} />
      {status !== 'signedIn' ? (
        <Glass style={styles.card}>
          <AppText tone="secondary">Sign in to see your tickets.</AppText>
          <PrimaryButton label="Sign in" onPress={() => router.push('/sign-in')} />
        </Glass>
      ) : upcoming.length === 0 ? (
        <Glass style={styles.card}>
          <AppText tone="secondary">{isPending ? 'Loading your tickets…' : "You haven't registered for anything coming up."}</AppText>
          <Link href="/discover">
            <AppText tone="interactive">Browse events</AppText>
          </Link>
        </Glass>
      ) : (
        <>
          {offline && (
            <AppText tone="tertiary" style={text.caption}>
              Offline: showing the tickets saved on this phone.
            </AppText>
          )}
          {upcoming.map((r) => (
            <Booking key={r.id} registration={r} />
          ))}
        </>
      )}
    </Screen>
  );
}

function Booking({ registration: r }: { registration: Registration }) {
  const { palette } = useTheme();
  const cancel = useCancelRegistration();
  const checkedIn = r.tickets.some((t) => t.checkedInAt);
  // Scanners need dark squares on a light background, whichever palette is showing.
  const dark = palette.appearance === 'dark';
  const ink = dark ? palette.color.background : palette.color.text.primary;
  const paper = dark ? palette.color.text.primary : palette.color.background;

  return (
    <Glass style={styles.card}>
      <Link href={{ pathname: '/event/[slug]', params: { slug: r.eventSlug } }}>
        <AppText style={[serif, styles.title]}>{r.eventTitle}</AppText>
      </Link>
      <AppText tone="tertiary" style={text.caption}>
        {eventWhen(r.startsAt)}
        {r.location ? ` · ${r.location.name}` : ''}
      </AppText>

      {r.status === 'Waitlisted' ? (
        <AppText tone="secondary">You're number {r.waitlistPosition} on the waiting list. We'll email you if a seat opens, and your tickets will appear here.</AppText>
      ) : (
        r.tickets.map((t) => (
          <View key={t.attendeeId} style={styles.ticket}>
            <View style={[styles.qr, { backgroundColor: paper }]} accessible accessibilityLabel={`Ticket QR code for ${t.name}`}>
              <QRCode value={ticketQrValue(t.code)} size={168} color={ink} backgroundColor={paper} ecl="M" />
            </View>
            <AppText style={text.headline}>{t.name}</AppText>
            <AppText tone={t.checkedInAt ? 'accent' : 'tertiary'} style={styles.code} selectable>
              {t.checkedInAt ? 'Checked in' : t.code}
            </AppText>
          </View>
        ))
      )}

      {!checkedIn && (
        <Pressable
          accessibilityRole="button"
          disabled={cancel.isPending}
          onPress={() =>
            Alert.alert('Cancel this booking?', r.tickets.length > 1 ? 'All tickets on this booking will be cancelled.' : undefined, [
              { text: 'Keep it', style: 'cancel' },
              { text: 'Cancel booking', style: 'destructive', onPress: () => cancel.mutate(r.id) },
            ])
          }>
          <AppText tone="danger" style={text.callout}>
            {cancel.isPending ? 'Cancelling…' : "Can't make it? Cancel"}
          </AppText>
        </Pressable>
      )}
    </Glass>
  );
}

const styles = StyleSheet.create({
  spacer: { height: space.xxl },
  card: { padding: space.lg, gap: space.sm },
  title: { fontSize: 22, lineHeight: 28 },
  ticket: { alignItems: 'center', gap: space.xs, paddingVertical: space.sm },
  qr: { padding: 14, borderRadius: 16 },
  code: { fontSize: 20, letterSpacing: 3, fontVariant: ['tabular-nums'] },
});
