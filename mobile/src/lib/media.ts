import AsyncStorage from '@react-native-async-storage/async-storage';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Directory, File, Paths } from 'expo-file-system';
import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import { api, unwrap, useSession, type Schemas } from './api';

export type SermonSummary = Schemas['SermonSummaryDto'];
export type SermonDetail = Schemas['SermonDetailDto'];
export type Series = Schemas['SeriesDto'];

export function useSermons(query: { q?: string; series?: string; page?: number; pageSize?: number } = {}) {
  return useQuery({
    queryKey: ['sermons', query],
    queryFn: async () =>
      unwrap(
        await api.GET('/api/media/sermons', {
          params: { query: { Q: query.q || undefined, Series: query.series, Page: query.page ?? 1, PageSize: query.pageSize ?? 20 } },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

export function useSermon(slug: string | undefined) {
  return useQuery({
    queryKey: ['sermon', slug],
    queryFn: async () => unwrap(await api.GET('/api/media/sermons/{slugOrId}', { params: { path: { slugOrId: slug! } } })),
    enabled: !!slug,
  });
}

export function useSeriesList() {
  return useQuery({ queryKey: ['series'], queryFn: async () => unwrap(await api.GET('/api/media/series')) });
}

export function useSeries(slug: string | undefined) {
  return useQuery({
    queryKey: ['series', slug],
    queryFn: async () => unwrap(await api.GET('/api/media/series/{slug}', { params: { path: { slug: slug! } } })),
    enabled: !!slug,
  });
}

export function useContinueListening() {
  const status = useSession((s) => s.status);
  return useQuery({
    queryKey: ['me', 'playback'],
    queryFn: async () => unwrap(await api.GET('/api/me/playback')),
    enabled: status === 'signedIn',
  });
}

export function formatMinutes(seconds: number | null | undefined): string | null {
  if (!seconds) return null;
  const minutes = Math.round(seconds / 60);
  return minutes >= 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes} min`;
}

export function formatDay(isoDate: string): string {
  return new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'long', year: 'numeric' }).format(new Date(isoDate));
}

// ---------- Settings ----------

interface MediaSettings {
  /** Low-data mode: prefer audio, never load video or large images automatically. */
  lowData: boolean;
  setLowData: (value: boolean) => void;
}

export const useMediaSettings = create<MediaSettings>()(
  persist((set) => ({ lowData: false, setLowData: (lowData) => set({ lowData }) }), {
    name: 'shapers.media-settings',
    storage: createJSONStorage(() => AsyncStorage),
  }),
);

// ---------- Offline downloads ----------

export interface DownloadedSermon {
  id: string;
  slug: string;
  title: string;
  speakers: string[];
  uri: string;
  sizeBytes: number;
  durationSeconds: number | null;
  artworkUrl: string | null;
}

interface DownloadState {
  items: Record<string, DownloadedSermon>;
  progress: Record<string, number>;
  download: (sermon: SermonDetail) => Promise<void>;
  remove: (id: string) => void;
}

const folder = () => {
  const dir = new Directory(Paths.document, 'sermons');
  if (!dir.exists) dir.create({ intermediates: true });
  return dir;
};

/** Sermon audio saved on the phone for listening without data. The list survives restarts. */
export const useDownloads = create<DownloadState>()(
  persist(
    (set, get) => ({
      items: {},
      progress: {},
      download: async (sermon) => {
        if (!sermon.audio || get().items[sermon.id] || get().progress[sermon.id] !== undefined) return;
        set((s) => ({ progress: { ...s.progress, [sermon.id]: 0 } }));
        try {
          const extension = sermon.audio.contentType.includes('mp4') || sermon.audio.contentType.includes('m4a') ? 'm4a' : 'mp3';
          const target = new File(folder(), `${sermon.id}.${extension}`);
          if (target.exists) target.delete();
          const file = await File.downloadFileAsync(sermon.audio.url, target);
          set((s) => ({
            items: {
              ...s.items,
              [sermon.id]: {
                id: sermon.id,
                slug: sermon.slug,
                title: sermon.title,
                speakers: sermon.speakers.map((x) => x.name),
                uri: file.uri,
                sizeBytes: sermon.audio?.sizeBytes ?? 0,
                durationSeconds: sermon.audio?.durationSeconds ?? null,
                artworkUrl: sermon.series?.artworkUrl ?? sermon.video?.thumbnailUrl ?? null,
              },
            },
          }));
        } finally {
          set((s) => {
            const { [sermon.id]: _, ...rest } = s.progress;
            return { progress: rest };
          });
        }
      },
      remove: (id) => {
        const item = get().items[id];
        if (item) {
          const file = new File(item.uri);
          if (file.exists) file.delete();
        }

        set((s) => {
          const { [id]: _, ...rest } = s.items;
          return { items: rest };
        });
      },
    }),
    {
      name: 'shapers.downloads',
      storage: createJSONStorage(() => AsyncStorage),
      partialize: (s) => ({ items: s.items }),
    },
  ),
);
