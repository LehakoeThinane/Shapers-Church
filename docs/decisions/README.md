# Architecture decision records

One short record per decision that would be expensive to reverse. Add a new record rather than editing an accepted one, and mark the old one "Superseded by NNNN".

| # | Decision | Status |
|---|---|---|
| [0001](0001-modular-monolith.md) | Modular monolith with Contracts between modules | Accepted |
| [0002](0002-single-tenant.md) | Single-tenant, organisation as scope root | Accepted |
| [0003](0003-scoped-permissions.md) | Permissions granted at scopes; paths as text | Accepted |
| [0004](0004-authentication.md) | Self-hosted identity: phone codes for members, 2FA for staff | Accepted |
| [0005](0005-outbox-and-jobs.md) | Transactional outbox and Hangfire | Accepted |
| [0006](0006-admin-portal-vite.md) | Admin portal as a React + Vite SPA | Accepted |
| [0007](0007-theme-tokens.md) | One token source for Midnight and Rose | Accepted |
| [0008](0008-popia-audit-and-consent.md) | Append-only audit log and consent records | Accepted |
| [0009](0009-hosting-region.md) | Host in Azure South Africa North | Accepted |
| [0010](0010-event-registration.md) | Event registration: seat locking, guest email codes and tickets | Accepted |
| [0011](0011-communications.md) | One Communications module for every message | Accepted |
| [0012](0012-privacy-rights-and-retention.md) | Privacy rights, erasure and retention | Accepted |
| [0013](0013-public-website.md) | A static public website built from the API | Accepted |
| [0014](0014-live-chat.md) | Moderated live chat during services | Accepted |
| [0015](0015-production-deployment.md) | Production on Azure: Container Apps, private Postgres, Key Vault, Static Web Apps | Accepted |
| [0016](0016-home-cells.md) | Home cells: leaders by membership, pastors by scoped permissions | Accepted |
| [0017](0017-ai-drafting.md) | AI drafts for staff only, from public church content, within a monthly budget | Accepted |
| [0018](0018-translations.md) | Translations as linked copies, checked by a speaker before publishing | Accepted |
| [0019](0019-services.md) | Services: plans, teams, scheduling with safeguarding, songs, live run sheet | Accepted |
| [0020](0020-kids-check-in.md) | Kids check-in: parents check in, a pickup code hands children back | Proposed |
| [0021](0021-giving.md) | Giving: card gifts through a provider's page, EFT and cash recorded by the finance team | Proposed |
