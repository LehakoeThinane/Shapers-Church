import { useQuery } from '@tanstack/react-query';

import { api, unwrap, type Schemas } from './api';

export type PostSummary = Schemas['PostSummaryDto'];

/** Church news still in date, for Home. */
export function useNews(count = 3) {
  return useQuery({
    queryKey: ['news', count],
    queryFn: async () => unwrap(await api.GET('/api/content/news', { params: { query: { count } } })),
    staleTime: 10 * 60_000,
  });
}

export function usePost(slug: string | undefined) {
  return useQuery({
    queryKey: ['post', slug],
    enabled: !!slug,
    queryFn: async () => unwrap(await api.GET('/api/content/posts/{slug}', { params: { path: { slug: slug! } } })),
  });
}
