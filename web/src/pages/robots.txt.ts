import type { APIRoute } from 'astro';

/** Search engines may read everything public; private pages (tickets, thank-you pages) say noindex themselves. */
export const GET: APIRoute = ({ site }) =>
  new Response(`User-agent: *\nAllow: /\n\nSitemap: ${new URL('/sitemap.xml', site).href}\n`, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });