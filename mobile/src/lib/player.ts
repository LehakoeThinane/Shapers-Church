import AsyncStorage from '@react-native-async-storage/async-storage';
import { createAudioPlayer, setAudioModeAsync, type AudioPlayer } from 'expo-audio';
import { Platform } from 'react-native';
import { create } from 'zustand';

import { api, useSession } from './api';
import { useDownloads, type SermonDetail } from './media';

/** iOS 26 has a slot in the tab bar for the mini-player; everywhere else it floats above the tab bar. */
export const hasTabAccessory = Platform.OS === 'ios' && Number.parseInt(String(Platform.Version), 10) >= 26;

export interface NowPlaying {
  id: string;
  slug: string;
  title: string;
  speakers: string[];
  artworkUrl: string | null;
  durationSeconds: number | null;
  offline: boolean;
}

interface PlayerState {
  current: NowPlaying | null;
  rate: number;
  setCurrent: (current: NowPlaying | null) => void;
  setRate: (rate: number) => void;
}

export const usePlayerStore = create<PlayerState>((set) => ({
  current: null,
  rate: 1,
  setCurrent: (current) => set({ current }),
  setRate: (rate) => set({ rate }),
}));

let player: AudioPlayer | null = null;
let saveTimer: ReturnType<typeof setInterval> | null = null;
const LOCAL_POSITION = (id: string) => `shapers.position.${id}`;

/** One player for the whole app, so audio keeps going while members move between screens or lock the phone. */
export function getPlayer(): AudioPlayer {
  if (!player) {
    void setAudioModeAsync({ playsInSilentMode: true, shouldPlayInBackground: true, interruptionMode: 'doNotMix' });
    player = createAudioPlayer(null, { updateInterval: 500 });
  }

  return player;
}

/**
 * Plays a sermon: the downloaded copy when there is one (no data used), otherwise streams.
 * Resumes from where the member stopped, on this phone or (when signed in) another.
 */
export async function playSermon(sermon: SermonDetail, resumeAt?: number) {
  if (!sermon.audio) return;
  const p = getPlayer();
  const download = useDownloads.getState().items[sermon.id];
  const artworkUrl = sermon.series?.artworkUrl ?? sermon.video?.thumbnailUrl ?? null;

  p.replace({ uri: download?.uri ?? sermon.audio.url });
  const local = Number(await AsyncStorage.getItem(LOCAL_POSITION(sermon.id)).catch(() => null)) || 0;
  const start = resumeAt ?? local;
  if (start > 0) await p.seekTo(start);
  p.setPlaybackRate(usePlayerStore.getState().rate);
  p.play();
  p.setActiveForLockScreen(true, {
    title: sermon.title,
    artist: sermon.speakers.map((s) => s.name).join(', ') || 'Shapers Church',
    albumTitle: sermon.series?.title ?? 'Shapers Church',
    ...(artworkUrl ? { artworkUrl } : {}),
  });

  usePlayerStore.getState().setCurrent({
    id: sermon.id,
    slug: sermon.slug,
    title: sermon.title,
    speakers: sermon.speakers.map((s) => s.name),
    artworkUrl,
    durationSeconds: sermon.audio.durationSeconds ?? null,
    offline: !!download,
  });
  startSavingPosition(sermon.id);
}

export function togglePlayback() {
  const p = getPlayer();
  if (p.playing) {
    p.pause();
    void savePosition();
  } else {
    p.play();
  }
}

export async function skip(seconds: number) {
  const p = getPlayer();
  await p.seekTo(Math.max(0, p.currentTime + seconds));
}

export function cycleRate() {
  const rates = [1, 1.25, 1.5, 2, 0.75];
  const next = rates[(rates.indexOf(usePlayerStore.getState().rate) + 1) % rates.length] ?? 1;
  getPlayer().setPlaybackRate(next);
  usePlayerStore.getState().setRate(next);
}

export function stopPlayback() {
  void savePosition();
  player?.pause();
  player?.setActiveForLockScreen(false);
  usePlayerStore.getState().setCurrent(null);
  if (saveTimer) clearInterval(saveTimer);
}

function startSavingPosition(sermonId: string) {
  if (saveTimer) clearInterval(saveTimer);
  saveTimer = setInterval(() => {
    if (player?.playing && usePlayerStore.getState().current?.id === sermonId) void savePosition();
  }, 15_000);
}

/** Keeps the place on the phone always, and on the account when signed in (quietly; offline is fine). */
async function savePosition() {
  const current = usePlayerStore.getState().current;
  if (!current || !player) return;
  const position = Math.floor(player.currentTime);
  await AsyncStorage.setItem(LOCAL_POSITION(current.id), String(position)).catch(() => undefined);
  if (useSession.getState().status === 'signedIn') {
    await api.PUT('/api/me/playback/{sermonId}', { params: { path: { sermonId: current.id } }, body: { positionSeconds: position } }).catch(() => undefined);
  }
}
