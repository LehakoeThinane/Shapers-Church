# Acknowledged dependency advisories

`pnpm audit --audit-level high` runs in CI. An advisory is listed in `package.json` (`pnpm.auditConfig.ignoreGhsas`) only when **no patched version exists** and the affected code **does not run in production**. Remove each entry as soon as a fix is released, and review this list monthly.

| Advisory | Package | Why it doesn't affect production | Acknowledged | Review by |
|---|---|---|---|---|
| [GHSA-86w9-cpqp-85rv](https://github.com/advisories/GHSA-86w9-cpqp-85rv) | `node-forge` ≤ 1.4.0 | Used only by the Expo command-line tools on developers' machines and in CI (code-signing certificates). Not part of the app installed on phones. | 2026-10-05 | 2026-11-05 |
| [GHSA-ch52-4w7c-c8xp](https://github.com/advisories/GHSA-ch52-4w7c-c8xp) | `http-cache-semantics` ≤ 4.2.0 | Used by Astro while the website is built. The live website is static files; no Astro server runs. | 2026-10-05 | 2026-11-05 |
| [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) | `braces` ≤ 3.0.3 | Used by file-matching in build tools (Metro, Vite). The denial of service needs an attacker-supplied pattern; we only use our own fixed patterns. | 2026-10-05 | 2026-11-05 |
