// Writes dist/staticwebapp.config.json for Azure Static Web Apps after the build:
// - old WordPress addresses redirect (301) to where the content lives now,
// - every guest ticket link (/events/tickets/{id}?key=...) is served by the one ticket page,
// - security headers, including a content security policy that only allows our API, YouTube and Google Fonts.
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const apiUrl = (process.env.API_URL ?? 'http://localhost:5080').replace(/\/$/, '');
const publicApi = (process.env.PUBLIC_API_URL ?? apiUrl).replace(/\/$/, '');
const offlineAllowed = process.env.ALLOW_OFFLINE_BUILD === '1';

let redirects = [];
try {
  const response = await fetch(`${apiUrl}/api/content/redirects`);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  redirects = await response.json();
} catch (error) {
  if (!offlineAllowed) throw new Error(`Couldn't read redirects from ${apiUrl}: ${error}`);
}

// Astro may inline small scripts; hash them so the policy can allow exactly those and nothing else.
const inlineHashes = new Set();
const { createHash } = await import('node:crypto');
const walk = (dir) =>
  readdirSync(dir, { withFileTypes: true }).flatMap((entry) => (entry.isDirectory() ? walk(join(dir, entry.name)) : [join(dir, entry.name)]));
if (existsSync('dist')) {
  for (const file of walk('dist').filter((f) => f.endsWith('.html'))) {
    const html = readFileSync(file, 'utf8');
    for (const match of html.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g)) {
      if (match[1].trim()) inlineHashes.add(`'sha256-${createHash('sha256').update(match[1]).digest('base64')}'`);
    }
  }
}

const csp = [
  "default-src 'self'",
  `script-src 'self' ${[...inlineHashes].join(' ')}`.trim(),
  "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
  "font-src 'self' https://fonts.gstatic.com",
  "img-src 'self' https: data:",
  "media-src 'self' https:",
  "frame-src https://www.youtube-nocookie.com",
  `connect-src 'self' ${publicApi}`,
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ');

const config = {
  routes: [
    ...redirects.map((r) => ({ route: r.from.replace(/\/$/, '') || '/', redirect: r.to, statusCode: 301 })),
    { route: '/events/tickets/*', rewrite: '/events/tickets/index.html' },
  ],
  responseOverrides: { 404: { rewrite: '/404.html' } },
  globalHeaders: {
    'Content-Security-Policy': csp,
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'strict-origin-when-cross-origin',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
  },
  mimeTypes: { '.json': 'application/json' },
};

writeFileSync('dist/staticwebapp.config.json', JSON.stringify(config, null, 2));
console.log(`staticwebapp.config.json: ${redirects.length} redirects, ${inlineHashes.size} inline scripts allowed`);
