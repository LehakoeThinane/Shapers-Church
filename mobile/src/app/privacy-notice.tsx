import { space } from '@shapers/tokens';
import { router, useLocalSearchParams } from 'expo-router';
import { ActivityIndicator, ScrollView, StyleSheet, View } from 'react-native';

import { PrimaryButton } from '@/components/glass';
import { GlowBackground } from '@/components/glow-background';
import { Notes } from '@/components/sermon-bits';
import { AppText } from '@/components/text';
import { errorMessage, useSession } from '@/lib/api';
import { useAcceptNotice, usePrivacyNotice } from '@/lib/privacy';
import { text } from '@/theme/type';

const dateFormat = new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'long', year: 'numeric' });

/**
 * The privacy notice. Opened from sign-up and Profile to read, or on launch (review=1) when the notice has changed
 * since the member last agreed to it.
 */
export default function PrivacyNoticeScreen() {
  const { review } = useLocalSearchParams<{ review?: string }>();
  const signedIn = useSession((s) => s.status) === 'signedIn';
  const notice = usePrivacyNotice();
  const accept = useAcceptNotice();
  const reviewing = review === '1' && signedIn;

  return (
    <View style={styles.root}>
      <GlowBackground />
      <ScrollView contentContainerStyle={styles.content}>
        {reviewing && (
          <AppText tone="accent" style={text.headline}>
            We&apos;ve updated how we look after your information. Please read it before you carry on.
          </AppText>
        )}
        {notice.isPending ? (
          <ActivityIndicator />
        ) : notice.isError ? (
          <AppText tone="secondary">We couldn&apos;t load the notice. Check your connection, or email info@shaperschurch.com for a copy.</AppText>
        ) : (
          <>
            <AppText tone="tertiary" style={text.caption}>
              Version {notice.data.version}, from {dateFormat.format(new Date(notice.data.effectiveFrom))}
            </AppText>
            <Notes markdown={notice.data.markdown} />
          </>
        )}
        {accept.error && <AppText tone="danger">{errorMessage(accept.error)}</AppText>}
        {reviewing && notice.data ? (
          <PrimaryButton
            label="I've read it"
            busy={accept.isPending}
            onPress={() => accept.mutate(notice.data.version, { onSuccess: () => router.back() })}
          />
        ) : (
          <PrimaryButton label="Close" onPress={() => router.back()} />
        )}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, overflow: 'hidden' },
  content: { padding: space.lg, paddingTop: space.xxl, paddingBottom: 80, gap: space.lg },
});
