# 0013. A static public website built from the API

Status: Accepted (2026-09-30)

## Context
The church's WordPress site (shaperschurch.com) has out-of-date details, a broken PayPal link and content that duplicates what the platform now holds: sermons, events, news and pages. Visitors are often on mobile data. The site must show public content only; personal data stays in the API in South Africa North (ADR 0009).

## Decision
- **Astro, built to static files**, in `web/`. Each build reads sermons, events, posts and pages from the API. Pages load fast, cost little to host and have no server to attack.
- **Anything that changes by the minute is fetched in the visitor's browser**, straight from the API: whether a service is live, seats left, guest bookings, the ticket page and the connect form.
- **Rebuilds:**
  - Every hour.
  - When website code changes on `main`.
  - Within minutes of a page, post or sermon going live. The API calls GitHub's repository dispatch when `Content:SiteRebuild` is configured.
- **Hosting:** Azure Static Web Apps behind Cloudflare. The deployment token and API address live in GitHub settings, never in the repository.
- **Security:**
  - Staff Markdown is rendered with raw HTML escaped and only web, email and phone links allowed.
  - The generated configuration sets a strict content security policy: our own scripts (with inline scripts allowed by hash), the API, YouTube's privacy-enhanced player and Google Fonts.
- **Guest ticket links** (`/events/tickets/{id}?key=…`) are served by a single page, which reads the key in the browser and sends it only to the API.
- **Migration from WordPress:** a one-off import brings the articles across (published, original dates) and the pages as drafts for checking. The old addresses are stored, and the site redirects them (301) to the new ones.
- **Builds fail loudly.** A build that can't reach the API fails rather than publishing an empty site. Only CI's compile check may build offline.

## Consequences
- Content changes appear after a rebuild, usually within a few minutes, not instantly. Live status and bookings are always current.
- Images in imported content still point at the old WordPress uploads. They must be copied to our storage before the WordPress site is switched off.
- The website needs the API's public address and a CORS entry for its own address.
