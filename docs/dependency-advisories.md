# Acknowledged dependency advisories

`pnpm audit --audit-level high` runs in CI. An advisory is listed in `package.json` (`pnpm.auditConfig.ignoreGhsas`) only when the affected code **does not run in production** and either **no patched version exists** or the patched version is **less than two weeks old** (our rule for new package versions). Remove each entry as soon as a fix is released and old enough, and review this list monthly.

| Advisory | Package | Why it doesn't affect production | Acknowledged | Review by |
|---|---|---|---|---|
| [GHSA-86w9-cpqp-85rv](https://github.com/advisories/GHSA-86w9-cpqp-85rv) | `node-forge` ≤ 1.4.0 | Used only by the Expo command-line tools on developers' machines and in CI (code-signing certificates). Not part of the app installed on phones. | 2026-10-05 | 2026-11-05 |
| [GHSA-ch52-4w7c-c8xp](https://github.com/advisories/GHSA-ch52-4w7c-c8xp) | `http-cache-semantics` ≤ 4.2.0 | Used by Astro while the website is built. The live website is static files; no Astro server runs. | 2026-10-05 | 2026-11-05 |
| [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) | `braces` ≤ 3.0.3 | Used by file-matching in build tools (Metro, Vite). The denial of service needs an attacker-supplied pattern; we only use our own fixed patterns. | 2026-10-05 | 2026-11-05 |
| [GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5) | `zod` < 4.6.6 | 3.25.x is used by the Expo command-line tools to check their own settings (no 3.x fix exists; 4.x is a different major version). Astro's copy is overridden to the fixed 4.6.6. Neither runs on the live website, the API or phones. | 2026-10-24 | 2026-11-24 |
| [GHSA-xjj6-f33q-929c](https://github.com/advisories/GHSA-xjj6-f33q-929c) | `undici` < 8.12.2 | Used by Astro's font loader while the website is built, to fetch the site's own fonts. The live website is static files. | 2026-10-24 | 2026-10-29: override to 8.12.2 once it is two weeks old |
