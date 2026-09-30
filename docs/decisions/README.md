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
