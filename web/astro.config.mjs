// @ts-check
import { defineConfig } from 'astro/config';

// A static site: every page is built from the API ahead of time, and rebuilt when content changes.
// Things that change by the minute (live status, seats left, guest bookings) are fetched in the browser.
export default defineConfig({
  site: process.env.SITE_URL ?? 'https://shaperschurch.com',
  output: 'static',
  trailingSlash: 'never',
  build: { format: 'directory' },
  server: { port: 4321 },
});
