import type { APIRoute } from 'astro';
import { get, getAll, type Schemas } from '../lib/api';
import { reservedSlugs } from '../lib/church';

/** Every public page, so search engines find new articles, sermons and events without stumbling on them. */
export const GET: APIRoute = async ({ site }) => {
  const [posts, sermons, events, pages] = await Promise.all([
    getAll<Schemas['PostSummaryDto']>('/api/content/posts'),
    getAll<Schemas['SermonSummaryDto']>('/api/media/sermons'),
    get<Schemas['EventDto'][]>('/api/events', []),
    get<Schemas['PageDto'][]>('/api/content/pages', []),
  ]);
  const fixed = ['/', '/what-to-expect', '/about', '/growth-track', '/sermons', '/events', '/whats-on', '/blog', '/give', '/live', '/connect', '/contact', '/privacy'];
  const entries: { path: string; modified?: string }[] = [
    ...fixed.map((path) => ({ path })),
    ...pages.filter((p) => !reservedSlugs.has(p.slug)).map((p) => ({ path: `/${p.slug}` })),
    ...posts.map((p) => ({ path: `/blog/${p.slug}`, modified: p.publishedAt })),
    ...sermons.map((s) => ({ path: `/sermons/${s.slug}`, modified: s.preachedOn })),
    ...events.filter((e) => e.visibility === 'Public').map((e) => ({ path: `/events/${e.slug}` })),
  ];
  const escape = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');
  const xml =
    '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
    entries
      .map((e) => `  <url><loc>${escape(new URL(e.path, site).href)}</loc>${e.modified ? `<lastmod>${e.modified.slice(0, 10)}</lastmod>` : ''}</url>`)
      .join('\n') +
    '\n</urlset>\n';
  return new Response(xml, { headers: { 'Content-Type': 'application/xml; charset=utf-8' } });
};