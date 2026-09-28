import { space, type PalettePreference } from '@shapers/tokens';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';

import { Glass, PrimaryButton } from '@/components/glass';
import { GlowBackground } from '@/components/glow-background';
import { AppText } from '@/components/text';
import { api, signOut, unwrap, useSession } from '@/lib/api';
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

        {status === 'signedIn' && (
          <Pressable
            accessibilityRole="button"
            onPress={async () => {
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

const styles = StyleSheet.create({
  root: { flex: 1, overflow: 'hidden' },
  content: { padding: space.lg, paddingTop: space.xxl, gap: space.xl },
  card: { padding: space.lg, gap: space.sm },
  section: { gap: space.sm },
  options: { padding: space.xs, gap: 2 },
  option: { paddingVertical: 12, paddingHorizontal: space.md, borderRadius: 16, gap: 2 },
  center: { textAlign: 'center' },
});
