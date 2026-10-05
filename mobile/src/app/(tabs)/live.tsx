import { radius, space } from '@shapers/tokens';
import * as WebBrowser from 'expo-web-browser';
import { Link } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { WebView } from 'react-native-webview';

import { ConnectForm } from '@/components/connect-form';
import { Glass, PrimaryButton } from '@/components/glass';
import { LiveChat } from '@/components/live-chat';
import { Screen } from '@/components/screen';
import { Notes } from '@/components/sermon-bits';
import { AppText, Icon } from '@/components/text';
import { FALLBACK_GIVE_URL, startsIn, useLiveNow, type PublicStream } from '@/lib/live';
import { useMediaSettings, useSermons } from '@/lib/media';
import { useTheme } from '@/theme/theme';
import { serif, text } from '@/theme/type';

type Panel = 'Chat' | 'Notes' | 'Bible' | 'Prayer' | 'Connect';
const panels: Panel[] = ['Chat', 'Notes', 'Bible', 'Prayer', 'Connect'];

export default function LiveScreen() {
  const live = useLiveNow();
  const stream = live.data?.stream;

  if (live.isPending) {
    return (
      <Screen>
        <ActivityIndicator />
      </Screen>
    );
  }

  if (live.data?.state === 'Live' && stream) return <LiveNow stream={stream} />;
  if (live.data?.state === 'Upcoming' && stream) return <Upcoming stream={stream} />;
  return <Nothing offline={live.isError} />;
}

function LiveNow({ stream }: { stream: PublicStream }) {
  const { palette } = useTheme();
  const lowData = useMediaSettings((s) => s.lowData);
  const [loadVideo, setLoadVideo] = useState(!lowData);
  const [panel, setPanel] = useState<Panel>('Chat');

  return (
    <Screen>
      <Glass cornerRadius={radius.card} style={styles.playerCard}>
        <View style={[styles.video, { backgroundColor: palette.color.video.middle }]}>
          {loadVideo && stream.youTubeId ? (
            <WebView
              source={{ uri: `https://www.youtube-nocookie.com/embed/${stream.youTubeId}?playsinline=1&autoplay=1&rel=0` }}
              allowsInlineMediaPlayback
              mediaPlaybackRequiresUserAction={false}
              allowsFullscreenVideo
              style={styles.webview}
            />
          ) : (
            <Pressable accessibilityRole="button" style={styles.videoPrompt} onPress={() => setLoadVideo(true)}>
              <Icon name={{ ios: 'play.circle.fill', android: 'play_circle' }} size={56} tone="primary" />
              <AppText tone="secondary" style={text.caption}>
                Low-data mode is on. Tap to watch (uses data).
              </AppText>
            </Pressable>
          )}
          <View style={[styles.pill, { backgroundColor: palette.color.accent }]} pointerEvents="none">
            <View style={[styles.dot, { backgroundColor: palette.color.text.onAccent }]} />
            <AppText style={[text.label, { color: palette.color.text.onAccent }]}>Live</AppText>
          </View>
        </View>
        <View style={styles.meta}>
          <AppText style={[text.headline, styles.flex]} numberOfLines={2}>
            {stream.title}
          </AppText>
          <GiveButton url={stream.giveUrl} />
        </View>
      </Glass>

      {stream.onScreen && (
        <Glass style={styles.scripture}>
          <AppText tone="accent" style={text.label}>
            {stream.onScreen.reference}
          </AppText>
          {stream.onScreen.text && <AppText style={[serif, styles.verse]}>{stream.onScreen.text}</AppText>}
        </Glass>
      )}

      <Glass cornerRadius={radius.pill} style={styles.switcher}>
        {panels.map((p) => (
          <Pressable
            key={p}
            accessibilityRole="tab"
            accessibilityState={{ selected: panel === p }}
            onPress={() => setPanel(p)}
            style={[styles.segment, panel === p && { backgroundColor: palette.color.glass.fillActive }]}>
            <AppText tone={panel === p ? 'primary' : 'secondary'} style={text.callout}>
              {p}
            </AppText>
          </Pressable>
        ))}
      </Glass>

      {panel === 'Chat' && <LiveChat livestreamId={stream.id} onPrayer={() => setPanel('Prayer')} />}

      {panel === 'Notes' &&
        (stream.notes ? (
          <Glass style={styles.card}>
            <Notes markdown={stream.notes} />
          </Glass>
        ) : (
          <AppText tone="tertiary">No notes for this service.</AppText>
        ))}

      {panel === 'Bible' && (
        <View style={styles.list}>
          {stream.shown.length === 0 && <AppText tone="tertiary">Scripture read in the service will appear here.</AppText>}
          {[...stream.shown].reverse().map((c) => (
            <Glass key={c.id} style={styles.card}>
              <AppText tone="interactive" style={text.label}>
                {c.reference}
              </AppText>
              {c.text && <AppText style={serif}>{c.text}</AppText>}
            </Glass>
          ))}
          <AppText tone="tertiary" style={text.caption}>
            Scripture quotations from the World English Bible (public domain).
          </AppText>
        </View>
      )}

      {panel === 'Prayer' && (
        <ConnectForm reasons={['Prayer']} initial={['Prayer']} messageLabel="How can we pray for you? (Only the pastoral team sees this.)" source="livestream" sourceId={stream.id} />
      )}

      {panel === 'Connect' && (
        <ConnectForm
          reasons={['FirstTime', 'Decision', 'MoreInfo', 'JoinGroup', 'Serve']}
          messageLabel="Anything you'd like us to know?"
          source="livestream"
          sourceId={stream.id}
        />
      )}
    </Screen>
  );
}

function Upcoming({ stream }: { stream: PublicStream }) {
  const [now, setNow] = useState(Date.now());
  const [praying, setPraying] = useState(false);
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(t);
  }, []);

  return (
    <Screen>
      <AppText style={text.largeTitle}>Live</AppText>
      <Glass style={styles.card}>
        <AppText tone="accent" style={text.label}>
          {startsIn(stream.scheduledStart, now)}
        </AppText>
        <AppText style={[serif, styles.upcomingTitle]}>{stream.title}</AppText>
        <AppText tone="secondary">Join us live here. We'll be starting soon.</AppText>
      </Glass>
      <GiveButton url={stream.giveUrl} wide />
      <AppText style={text.headline}>Chat</AppText>
      <LiveChat livestreamId={stream.id} onPrayer={() => setPraying(true)} />
      {praying && (
        <ConnectForm reasons={['Prayer']} initial={['Prayer']} messageLabel="How can we pray for you? (Only the pastoral team sees this.)" source="livestream" sourceId={stream.id} />
      )}
      <Glass style={styles.card}>
        <AppText style={text.headline}>New to Shapers?</AppText>
        <AppText tone="secondary">We'd love to meet you. Let us know you're watching.</AppText>
      </Glass>
      <ConnectForm reasons={['FirstTime', 'MoreInfo', 'Prayer']} messageLabel="Anything you'd like us to know?" source="livestream" sourceId={stream.id} />
    </Screen>
  );
}

function Nothing({ offline }: { offline: boolean }) {
  const latest = useSermons({ pageSize: 1 }).data?.items[0];
  return (
    <Screen>
      <AppText style={text.largeTitle}>Live</AppText>
      <Glass style={styles.card}>
        <AppText style={text.headline}>{offline ? "You're offline" : 'Nothing live right now'}</AppText>
        <AppText tone="secondary">
          {offline ? 'Connect to watch services live.' : 'Sunday services stream here. Catch up on the latest message in the meantime.'}
        </AppText>
      </Glass>
      {latest && (
        <Link href={{ pathname: '/sermon/[slug]', params: { slug: latest.slug } }} asChild>
          <Pressable accessibilityRole="button">
            <Glass style={styles.card}>
              <AppText tone="accent" style={text.label}>
                Latest sermon
              </AppText>
              <AppText style={[serif, styles.upcomingTitle]}>{latest.title}</AppText>
              <AppText tone="tertiary" style={text.caption}>
                {latest.speakers.join(', ')}
              </AppText>
            </Glass>
          </Pressable>
        </Link>
      )}
    </Screen>
  );
}

function GiveButton({ url, wide = false }: { url: string | null | undefined; wide?: boolean }) {
  const open = () => void WebBrowser.openBrowserAsync(url ?? FALLBACK_GIVE_URL);
  return wide ? (
    <PrimaryButton label="Give" onPress={open} />
  ) : (
    <View style={styles.giveSmall}>
      <PrimaryButton label="Give" onPress={open} />
    </View>
  );
}

const styles = StyleSheet.create({
  playerCard: { padding: space.sm },
  video: { aspectRatio: 16 / 9, borderRadius: 22, overflow: 'hidden', justifyContent: 'center' },
  webview: { flex: 1, backgroundColor: 'transparent' },
  videoPrompt: { alignItems: 'center', gap: space.xs },
  pill: { position: 'absolute', top: 12, left: 12, flexDirection: 'row', alignItems: 'center', gap: 6, paddingHorizontal: 10, paddingVertical: 5, borderRadius: radius.pill },
  dot: { width: 6, height: 6, borderRadius: 3 },
  meta: { flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingHorizontal: space.xs, paddingTop: 12, paddingBottom: 4 },
  flex: { flex: 1 },
  giveSmall: { width: 96 },
  scripture: { padding: space.lg, gap: space.xs },
  verse: { fontSize: 19, lineHeight: 27 },
  switcher: { flexDirection: 'row', padding: 4 },
  segment: { flex: 1, alignItems: 'center', paddingVertical: 8, borderRadius: radius.pill },
  card: { padding: space.lg, gap: space.xs },
  list: { gap: space.sm },
  upcomingTitle: { fontSize: 24, lineHeight: 30 },
});
