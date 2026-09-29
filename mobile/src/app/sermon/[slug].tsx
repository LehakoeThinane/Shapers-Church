import { radius, space } from "@shapers/tokens";
import { useAudioPlayerStatus } from "expo-audio";
import { Link, Stack, useLocalSearchParams } from "expo-router";
import { useState } from "react";
import {
  ActivityIndicator,
  Pressable,
  Share,
  StyleSheet,
  View,
} from "react-native";
import { WebView } from "react-native-webview";

import { Glass, PrimaryButton } from "@/components/glass";
import { Screen } from "@/components/screen";
import { Artwork, Notes } from "@/components/sermon-bits";
import { AppText, Icon } from "@/components/text";
import {
  canDownload,
  formatDay,
  formatMinutes,
  useDownloads,
  useMediaSettings,
  useSermon,
} from "@/lib/media";
import {
  cycleRate,
  getPlayer,
  playSermon,
  skip,
  togglePlayback,
  usePlayerStore,
} from "@/lib/player";
import { useTheme } from "@/theme/theme";
import { serif, text } from "@/theme/type";

export default function SermonScreen() {
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const { palette } = useTheme();
  const sermon = useSermon(slug);
  const lowData = useMediaSettings((s) => s.lowData);
  const [showVideo, setShowVideo] = useState(false);
  const downloads = useDownloads();
  const current = usePlayerStore((s) => s.current);
  const rate = usePlayerStore((s) => s.rate);
  const status = useAudioPlayerStatus(getPlayer());

  const s = sermon.data;
  if (!s) {
    return (
      <Screen>
        <Stack.Screen options={{ title: "" }} />
        {sermon.isError ? (
          <AppText tone="secondary">
            We couldn't load this sermon. Check your connection.
          </AppText>
        ) : (
          <ActivityIndicator />
        )}
      </Screen>
    );
  }

  const isCurrent = current?.id === s.id;
  const downloaded = downloads.items[s.id];
  const downloading = downloads.progress[s.id] !== undefined;
  const artwork = s.series?.artworkUrl ?? s.video?.thumbnailUrl ?? null;

  return (
    <Screen>
      <Stack.Screen
        options={{
          title: "",
          headerTransparent: true,
          headerTintColor: palette.color.interactive,
        }}
      />

      <View style={styles.header}>
        <Artwork uri={artwork} title={s.title} size={96} />
        <View style={styles.flex}>
          {s.series && (
            <Link
              href={{
                pathname: "/series/[slug]",
                params: { slug: s.series.slug },
              }}
            >
              <AppText tone="interactive" style={text.label}>
                {s.series.title}
              </AppText>
            </Link>
          )}
          <AppText style={[serif, styles.title]}>{s.title}</AppText>
          <AppText tone="tertiary" style={text.caption}>
            {s.speakers.map((x) => x.name).join(", ")} ·{" "}
            {formatDay(s.preachedOn)}
          </AppText>
        </View>
      </View>

      {s.scripture.length > 0 && (
        <View style={styles.chips}>
          {s.scripture.map((r) => (
            <Glass
              key={r.display}
              cornerRadius={radius.pill}
              style={styles.chip}
            >
              <AppText style={text.callout}>{r.display}</AppText>
            </Glass>
          ))}
        </View>
      )}

      {s.audio && (
        <Glass style={styles.player}>
          {isCurrent ? (
            <>
              <View
                style={[styles.track, { backgroundColor: palette.color.track }]}
              >
                <View
                  style={[
                    styles.progress,
                    {
                      width: `${status.duration > 0 ? Math.round((status.currentTime / status.duration) * 100) : 0}%`,
                      backgroundColor:
                        palette.appearance === "dark"
                          ? palette.color.accent
                          : palette.color.interactive,
                    },
                  ]}
                />
              </View>
              <View style={styles.controls}>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Back 15 seconds"
                  onPress={() => void skip(-15)}
                  hitSlop={10}
                >
                  <Icon
                    name={{ ios: "gobackward.15", android: "replay_10" }}
                    size={28}
                  />
                </Pressable>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={status.playing ? "Pause" : "Play"}
                  onPress={togglePlayback}
                  hitSlop={10}
                >
                  <Icon
                    name={
                      status.playing
                        ? { ios: "pause.circle.fill", android: "pause_circle" }
                        : { ios: "play.circle.fill", android: "play_circle" }
                    }
                    size={56}
                  />
                </Pressable>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Forward 30 seconds"
                  onPress={() => void skip(30)}
                  hitSlop={10}
                >
                  <Icon
                    name={{ ios: "goforward.30", android: "forward_30" }}
                    size={28}
                  />
                </Pressable>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={`Speed ${rate} times`}
                  onPress={cycleRate}
                  hitSlop={10}
                >
                  <AppText tone="interactive" style={text.headline}>
                    {rate}×
                  </AppText>
                </Pressable>
              </View>
            </>
          ) : (
            <PrimaryButton
              label={`Listen${formatMinutes(s.audio.durationSeconds) ? ` · ${formatMinutes(s.audio.durationSeconds)}` : ""}`}
              onPress={() => void playSermon(s)}
            />
          )}

          {canDownload && (
            <Pressable
              accessibilityRole="button"
              disabled={downloading}
              onPress={() =>
                downloaded ? downloads.remove(s.id) : void downloads.download(s)
              }
              style={styles.download}
            >
              <Icon
                name={
                  downloaded
                    ? { ios: "checkmark.circle.fill", android: "download_done" }
                    : { ios: "arrow.down.circle", android: "download" }
                }
                size={20}
              />
              <AppText tone="interactive" style={text.callout}>
                {downloading
                  ? "Downloading…"
                  : downloaded
                    ? "Downloaded · tap to remove"
                    : `Download for offline (${Math.max(1, Math.round(s.audio.sizeBytes / (1024 * 1024)))} MB)`}
              </AppText>
            </Pressable>
          )}
        </Glass>
      )}

      {s.video && (
        <Glass style={styles.videoCard}>
          {showVideo ? (
            <View style={styles.video}>
              <WebView
                source={{
                  uri: `https://www.youtube-nocookie.com/embed/${s.video.externalId}?playsinline=1&rel=0`,
                }}
                allowsInlineMediaPlayback
                mediaPlaybackRequiresUserAction={false}
                allowsFullscreenVideo
                style={styles.webview}
              />
            </View>
          ) : (
            <Pressable
              accessibilityRole="button"
              onPress={() => setShowVideo(true)}
              style={styles.videoPrompt}
            >
              <Icon
                name={{ ios: "play.rectangle", android: "smart_display" }}
                size={26}
              />
              <View style={styles.flex}>
                <AppText style={text.headline}>Watch the video</AppText>
                <AppText tone="tertiary" style={text.caption}>
                  {lowData
                    ? "Uses a lot more data than audio"
                    : "Plays from YouTube"}
                </AppText>
              </View>
            </Pressable>
          )}
        </Glass>
      )}

      {s.summary && <AppText tone="secondary">{s.summary}</AppText>}

      {s.notes && (
        <View style={styles.section}>
          <AppText style={text.title}>Notes</AppText>
          <Glass style={styles.card}>
            <Notes markdown={s.notes} />
          </Glass>
        </View>
      )}

      <Pressable
        accessibilityRole="button"
        onPress={() =>
          void Share.share({
            message: `${s.title}: https://shaperschurch.com/sermons/${s.slug}`,
          })
        }
        style={styles.download}
      >
        <Icon
          name={{ ios: "square.and.arrow.up", android: "share" }}
          size={20}
        />
        <AppText tone="interactive" style={text.callout}>
          Share this sermon
        </AppText>
      </Pressable>
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: "row",
    gap: space.md,
    alignItems: "center",
    marginTop: 40,
  },
  flex: { flex: 1, gap: 4 },
  title: { fontSize: 26, lineHeight: 32 },
  chips: { flexDirection: "row", flexWrap: "wrap", gap: space.xs },
  chip: { paddingHorizontal: 12, paddingVertical: 6 },
  player: { padding: space.md, gap: space.md },
  track: { height: 4, borderRadius: 4, overflow: "hidden" },
  progress: { height: "100%", borderRadius: 4 },
  controls: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-around",
  },
  download: {
    flexDirection: "row",
    alignItems: "center",
    gap: space.xs,
    alignSelf: "center",
    paddingVertical: 4,
  },
  videoCard: { overflow: "hidden" },
  video: { aspectRatio: 16 / 9, borderRadius: radius.xl, overflow: "hidden" },
  webview: { flex: 1, backgroundColor: "transparent" },
  videoPrompt: {
    flexDirection: "row",
    alignItems: "center",
    gap: space.md,
    padding: space.md,
  },
  section: { gap: space.sm },
  card: { padding: space.lg },
});
