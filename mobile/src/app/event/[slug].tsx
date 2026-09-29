import { space } from '@shapers/tokens';
import { Image } from 'expo-image';
import { router, Stack, useLocalSearchParams } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ActivityIndicator, Linking, Pressable, StyleSheet, Switch, View } from 'react-native';

import { DateBadge } from '@/components/event-bits';
import { Glass, PrimaryButton } from '@/components/glass';
import { Screen } from '@/components/screen';
import { Notes } from '@/components/sermon-bits';
import { AppText, Field } from '@/components/text';
import { api, errorMessage, unwrap, useSession } from '@/lib/api';
import { eventTime, eventWhen, seatsNote, useEvent, useHousehold, useMyRegistrations, useRegister, type ChurchEvent } from '@/lib/events';
import { useMediaSettings } from '@/lib/media';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

export default function EventScreen() {
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const { palette } = useTheme();
  const event = useEvent(slug);
  const e = event.data;

  return (
    <Screen>
      <Stack.Screen options={{ title: '', headerTransparent: true, headerTintColor: palette.color.interactive }} />
      {!e ? (
        event.isError ? (
          <AppText tone="secondary">We couldn't load this event. Check your connection.</AppText>
        ) : (
          <ActivityIndicator />
        )
      ) : (
        <Details event={e} />
      )}
    </Screen>
  );
}

function Details({ event: e }: { event: ChurchEvent }) {
  const lowData = useMediaSettings((s) => s.lowData);
  const mine = useMyRegistrations().upcoming.find((r) => r.eventId === e.id);
  const mapsUrl = e.location?.address ? `https://maps.google.com/?q=${encodeURIComponent(e.location.address)}` : null;

  return (
    <>
      {e.imageUrl && !lowData && <Image source={{ uri: e.imageUrl }} style={styles.image} contentFit="cover" accessibilityIgnoresInvertColors />}
      <View style={styles.header}>
        <DateBadge iso={e.startsAt} />
        <View style={styles.flex}>
          <AppText style={[serif, styles.title]}>{e.title}</AppText>
          <AppText tone="tertiary" style={text.caption}>
            {eventWhen(e.startsAt)} – {eventTime(e.endsAt)}
          </AppText>
        </View>
      </View>

      {e.location && (
        <Pressable accessibilityRole={mapsUrl ? 'link' : undefined} disabled={!mapsUrl} onPress={() => mapsUrl && Linking.openURL(mapsUrl)}>
          <Glass style={styles.card}>
            <AppText style={text.headline}>{e.location.name}</AppText>
            {e.location.address && <AppText tone="interactive">{e.location.address}</AppText>}
          </Glass>
        </Pressable>
      )}

      {e.summary && <AppText tone="secondary">{e.summary}</AppText>}
      {e.description && <Notes markdown={e.description} />}

      {e.registrationRequired &&
        (mine ? (
          <Glass style={styles.card}>
            <AppText style={text.headline}>{mine.status === 'Waitlisted' ? `You're on the waiting list (number ${mine.waitlistPosition})` : "You're booked"}</AppText>
            <AppText tone="secondary">{mine.tickets.map((t) => t.name).join(', ')}</AppText>
            <PrimaryButton label="Open my tickets" onPress={() => router.push('/tickets')} />
          </Glass>
        ) : (
          <RegisterCard event={e} />
        ))}
    </>
  );
}

function RegisterCard({ event: e }: { event: ChurchEvent }) {
  const status = useSession((s) => s.status);
  const household = useHousehold().data ?? [];
  const me = useQuery({
    queryKey: ['me', 'profile'],
    enabled: status === 'signedIn',
    queryFn: async () => unwrap(await api.GET('/api/me/profile')),
  }).data;
  const register = useRegister(e.slug);
  const [others, setOthers] = useState<string[]>([]);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const note = seatsNote(e);

  if (!e.registrationOpen) {
    return (
      <Glass style={styles.card}>
        <AppText tone="secondary">{note}</AppText>
      </Glass>
    );
  }

  if (status !== 'signedIn') {
    return (
      <Glass style={styles.card}>
        <AppText tone="secondary">{note}</AppText>
        <PrimaryButton label="Sign in to register" onPress={() => router.push('/sign-in')} />
      </Glass>
    );
  }

  const party = 1 + others.length;
  const missing = e.questions.some((q) => q.required && !answers[q.id]?.trim());

  return (
    <Glass style={styles.card}>
      <AppText style={text.headline}>{e.waitlistOnly ? 'Join the waiting list' : 'Register'}</AppText>
      <AppText tone="tertiary" style={text.caption}>
        {note}
      </AppText>

      {household.length > 0 && (
        <View style={styles.people}>
          <AppText tone="secondary" style={text.callout}>
            Who's coming? (up to {e.maxPerRegistration})
          </AppText>
          <PersonToggle label="You" value disabled onChange={() => {}} />
          {household.map((m) => (
            <PersonToggle
              key={m.personId}
              label={m.isChild ? `${m.displayName} (child)` : m.displayName}
              value={others.includes(m.personId)}
              disabled={!others.includes(m.personId) && party >= e.maxPerRegistration}
              onChange={(on) => setOthers(on ? [...others, m.personId] : others.filter((x) => x !== m.personId))}
            />
          ))}
        </View>
      )}

      {e.questions.map((q) => (
        <View key={q.id} style={styles.question}>
          <AppText tone="secondary" style={text.callout}>
            {q.label}
            {q.required ? '' : ' (optional)'}
          </AppText>
          <Field value={answers[q.id] ?? ''} onChangeText={(v) => setAnswers({ ...answers, [q.id]: v })} maxLength={500} />
        </View>
      ))}

      {register.error && <AppText tone="danger">{errorMessage(register.error)}</AppText>}
      {register.data ? (
        <AppText tone="accent" style={text.headline}>
          {register.data.status === 'Waitlisted' ? "You're on the waiting list. We'll email you if a seat opens." : "You're booked. Your tickets are ready."}
        </AppText>
      ) : (
        <PrimaryButton
          label={e.waitlistOnly ? 'Join waiting list' : party > 1 ? `Register ${party} people` : 'Register'}
          busy={register.isPending}
          disabled={missing || (others.length > 0 && !me)}
          onPress={() => register.mutate({ attendeePersonIds: others.length && me ? [me.id, ...others] : [], answers })}
        />
      )}
    </Glass>
  );
}

function PersonToggle({ label, value, disabled, onChange }: { label: string; value: boolean; disabled?: boolean; onChange: (value: boolean) => void }) {
  const { palette } = useTheme();
  return (
    <View style={styles.toggle}>
      <AppText style={styles.flex}>{label}</AppText>
      <Switch value={value} disabled={disabled} onValueChange={onChange} trackColor={{ true: palette.color.accent }} accessibilityLabel={label} />
    </View>
  );
}

const styles = StyleSheet.create({
  image: { width: '100%', aspectRatio: 16 / 9, borderRadius: 22 },
  header: { flexDirection: 'row', gap: space.md, alignItems: 'center', marginTop: space.xl },
  flex: { flex: 1, gap: 4 },
  title: { fontSize: 28, lineHeight: 34 },
  card: { padding: space.lg, gap: space.sm },
  people: { gap: space.xs },
  toggle: { flexDirection: 'row', alignItems: 'center', gap: space.sm },
  question: { gap: space.xs },
});
