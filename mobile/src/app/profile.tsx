import { space, type PalettePreference } from '@shapers/tokens';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, router } from 'expo-router';
import { useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, Switch, View } from 'react-native';

import { Glass, PrimaryButton } from '@/components/glass';
import { GlowBackground } from '@/components/glow-background';
import { AppText, Field } from '@/components/text';
import { api, errorMessage, signOut, unwrap, useSession } from '@/lib/api';
import { enablePush, pushSupported, releasePush, topicLabels, usePreferences, usePushPermission, useSetPreference } from '@/lib/notifications';
import { downloadMyData, requestLabel, useMyPrivacyRequests, useSubmitPrivacyRequest } from '@/lib/privacy';
import { formatMinutes, useDownloads, useMediaSettings } from '@/lib/media';
import { useTheme } from '@/theme/theme';
import { text } from '@/theme/type';

const paletteOptions: { value: PalettePreference; label: string; hint: string }[] = [
  { value: 'auto', label: 'Automatic', hint: 'Follows your phone' },
  { value: 'midnight', label: 'Midnight', hint: 'Dark, with gold' },
  { value: 'rose', label: 'Rose', hint: 'Light, with rose' },
];

export default function ProfileScreen() {
  const { palette, preference, setPreference } = useTheme();
  const status = useSession((s) => s.status);
  const queryClient = useQueryClient();
  const { lowData, setLowData } = useMediaSettings();
  const downloads = useDownloads();
  const downloaded = Object.values(downloads.items);
  const downloadedMb = Math.round(downloaded.reduce((sum, d) => sum + d.sizeBytes, 0) / (1024 * 1024));
  const profile = useQuery({
    queryKey: ['me', 'profile'],
    queryFn: async () => unwrap(await api.GET('/api/me/profile')),
    enabled: status === 'signedIn',
  });

  const choosePalette = (value: PalettePreference) => {
    setPreference(value);
    if (status === 'signedIn') {
      void api.PUT('/api/me/preferences', { body: { palette: value } });
    }
  };

  return (
    <View style={styles.root}>
      <GlowBackground />
      <ScrollView contentContainerStyle={styles.content}>
        {status === 'signedIn' ? (
          <Glass style={styles.card}>
            <AppText style={text.largeTitle}>{profile.data?.displayName ?? ' '}</AppText>
            <AppText tone="secondary">
              {profile.data ? `${profile.data.campusName ?? 'Shapers Church'} · ${profile.data.membershipStatus.name}` : 'Loading your profile…'}
            </AppText>
          </Glass>
        ) : (
          <Glass style={styles.card}>
            <AppText style={text.title}>Join in at Shapers</AppText>
            <AppText tone="secondary">Sign in with your phone number to give, join groups and see your serving schedule.</AppText>
            <PrimaryButton label="Sign in" onPress={() => router.push('/sign-in')} />
          </Glass>
        )}

        <View style={styles.section}>
          <AppText style={text.title}>Appearance</AppText>
          <Glass style={styles.options}>
            {paletteOptions.map((option) => {
              const selected = preference === option.value;
              return (
                <Pressable
                  key={option.value}
                  accessibilityRole="radio"
                  accessibilityState={{ selected }}
                  onPress={() => choosePalette(option.value)}
                  style={[styles.option, selected && { backgroundColor: palette.color.glass.fillActive }]}>
                  <AppText style={text.headline}>{option.label}</AppText>
                  <AppText tone="tertiary" style={text.caption}>
                    {option.hint}
                  </AppText>
                </Pressable>
              );
            })}
          </Glass>
        </View>

        {status === 'signedIn' && <NotificationSettings />}
        {status === 'signedIn' && <YourData />}

        <View style={styles.section}>
          <AppText style={text.title}>Data</AppText>
          <Glass style={styles.card}>
            <View style={styles.switchRow}>
              <View style={styles.flex}>
                <AppText style={text.headline}>Low-data mode</AppText>
                <AppText tone="tertiary" style={text.caption}>
                  Prefer audio, and don't load videos or large pictures until you ask.
                </AppText>
              </View>
              <Switch value={lowData} onValueChange={setLowData} trackColor={{ true: palette.color.accent }} />
            </View>
          </Glass>
        </View>

        <View style={styles.section}>
          <AppText style={text.title}>Downloads</AppText>
          <Glass style={styles.card}>
            {downloaded.length === 0 ? (
              <AppText tone="secondary">Download sermons to listen without using data.</AppText>
            ) : (
              <>
                <AppText tone="tertiary" style={text.caption}>
                  {downloaded.length} {downloaded.length === 1 ? 'sermon' : 'sermons'} · {downloadedMb} MB on this phone
                </AppText>
                {downloaded.map((d) => (
                  <View key={d.id} style={styles.switchRow}>
                    <Link href={{ pathname: '/sermon/[slug]', params: { slug: d.slug } }} style={styles.flex}>
                      <AppText style={text.callout}>
                        {d.title}
                        {formatMinutes(d.durationSeconds) ? ` · ${formatMinutes(d.durationSeconds)}` : ''}
                      </AppText>
                    </Link>
                    <Pressable accessibilityRole="button" accessibilityLabel={`Remove ${d.title}`} onPress={() => downloads.remove(d.id)} hitSlop={8}>
                      <AppText tone="interactive" style={text.callout}>
                        Remove
                      </AppText>
                    </Pressable>
                  </View>
                ))}
              </>
            )}
          </Glass>
        </View>

        {status === 'signedIn' && (
          <Pressable
            accessibilityRole="button"
            onPress={async () => {
              await releasePush();
              await signOut();
              queryClient.removeQueries({ queryKey: ['me'] });
              router.back();
            }}>
            <AppText tone="interactive" style={[text.headline, styles.center]}>
              Sign out
            </AppText>
          </Pressable>
        )}
      </ScrollView>
    </View>
  );
}

function YourData() {
  const requests = useMyPrivacyRequests();
  const submit = useSubmitPrivacyRequest();
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [correction, setCorrection] = useState('');
  const [correcting, setCorrecting] = useState(false);
  const openDeletion = requests.data?.some((r) => r.type === 'Deletion' && r.status === 'Open');

  return (
    <View style={styles.section}>
      <AppText style={text.title}>Your data</AppText>
      <Glass style={styles.card}>
        <AppText tone="secondary">
          See everything Shapers Church holds about you, ask us to correct it, or ask us to delete it. Our Information Officer answers
          within 30 days.
        </AppText>

        <Pressable
          accessibilityRole="button"
          disabled={downloading}
          onPress={() => {
            setDownloading(true);
            setDownloadError(null);
            downloadMyData()
              .catch((e: unknown) => setDownloadError(errorMessage(e)))
              .finally(() => setDownloading(false));
          }}>
          <AppText tone="interactive" style={text.headline}>
            {downloading ? 'Preparing your data…' : 'Download my data'}
          </AppText>
        </Pressable>
        {downloadError && <AppText tone="danger">{downloadError}</AppText>}

        {correcting ? (
          <View style={styles.section}>
            <Field multiline value={correction} onChangeText={setCorrection} maxLength={2000} placeholder="What needs correcting?" style={styles.textArea} />
            <View style={styles.switchRow}>
              <Pressable
                accessibilityRole="button"
                disabled={!correction.trim() || submit.isPending}
                onPress={() =>
                  submit.mutate(
                    { type: 'Correction', details: correction },
                    {
                      onSuccess: () => {
                        setCorrection('');
                        setCorrecting(false);
                      },
                    },
                  )
                }>
                <AppText tone="interactive" style={text.headline}>
                  Send
                </AppText>
              </Pressable>
              <Pressable accessibilityRole="button" onPress={() => setCorrecting(false)}>
                <AppText tone="tertiary" style={text.callout}>
                  Cancel
                </AppText>
              </Pressable>
            </View>
          </View>
        ) : (
          <Pressable accessibilityRole="button" onPress={() => setCorrecting(true)}>
            <AppText tone="interactive" style={text.headline}>
              Ask us to correct something
            </AppText>
          </Pressable>
        )}

        {!openDeletion && (
          <Pressable
            accessibilityRole="button"
            onPress={() =>
              Alert.alert(
                'Delete your account and data?',
                'Our Information Officer will delete your login, prayer requests, notifications and church record. Event bookings are kept without your name. This can\u2019t be undone.',
                [
                  { text: 'Keep my account', style: 'cancel' },
                  { text: 'Ask to delete', style: 'destructive', onPress: () => submit.mutate({ type: 'Deletion', details: null }) },
                ],
              )
            }>
            <AppText tone="danger" style={text.headline}>
              Delete my account and data
            </AppText>
          </Pressable>
        )}
        {submit.error && <AppText tone="danger">{errorMessage(submit.error)}</AppText>}

        {requests.data?.map((r) => (
          <View key={r.id} style={styles.flex}>
            <AppText style={text.callout}>{requestLabel(r)}</AppText>
            {r.response && (
              <AppText tone="tertiary" style={text.caption}>
                {r.response}
              </AppText>
            )}
          </View>
        ))}
      </Glass>
    </View>
  );
}

function NotificationSettings() {
  const { palette } = useTheme();
  const permission = usePushPermission();
  const preferences = usePreferences();
  const setPreference = useSetPreference();
  const push = (preferences.data ?? []).filter((p) => p.channel === 'Push');
  const allowed = permission.data === 'granted' && push.some((p) => p.consentGiven);

  return (
    <View style={styles.section}>
      <AppText style={text.title}>Notifications</AppText>
      <Glass style={styles.card}>
        {!pushSupported ? (
          <AppText tone="secondary">
            Notifications need the Shapers Church app from the store. You'll still find everything in the notifications inbox on Home.
          </AppText>
        ) : !allowed ? (
          <>
            <AppText tone="secondary">
              {permission.data === 'denied'
                ? "Notifications are turned off for this app in your phone's settings."
                : 'Get a notification when a service goes live, a sermon is published, or your booking changes.'}
            </AppText>
            {permission.data !== 'denied' && (
              <Pressable accessibilityRole="button" onPress={() => void enablePush().finally(() => void permission.refetch().then(() => preferences.refetch()))}>
                <AppText tone="interactive" style={text.headline}>
                  Turn on notifications
                </AppText>
              </Pressable>
            )}
          </>
        ) : (
          push.map((p) => (
            <View key={p.topic} style={styles.switchRow}>
              <View style={styles.flex}>
                <AppText style={text.headline}>{topicLabels[p.topic].title}</AppText>
                <AppText tone="tertiary" style={text.caption}>
                  {topicLabels[p.topic].hint}
                </AppText>
              </View>
              <Switch
                value={p.enabled}
                onValueChange={(enabled) => setPreference.mutate({ topic: p.topic, channel: 'Push', enabled })}
                trackColor={{ true: palette.color.accent }}
                accessibilityLabel={topicLabels[p.topic].title}
              />
            </View>
          ))
        )}
      </Glass>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, overflow: 'hidden' },
  content: { padding: space.lg, paddingTop: space.xxl, gap: space.xl },
  card: { padding: space.lg, gap: space.sm },
  section: { gap: space.sm },
  options: { padding: space.xs, gap: 2 },
  option: { paddingVertical: 12, paddingHorizontal: space.md, borderRadius: 16, gap: 2 },
  center: { textAlign: 'center' },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  flex: { flex: 1, gap: 2 },
  textArea: { height: 100, paddingTop: 14, textAlignVertical: 'top' },
});
