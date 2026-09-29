# Phase 0 Plan: Foundation

Status: **Approved and in progress.** Backend, tokens, API client and CI are done; admin and mobile shells are being filled in. Where the build differs from the original proposal, this document has been updated to match the code.

Goal: a running skeleton where a staff member can sign in to the admin portal and see people for the campuses they have permission for, and a member can sign in to the app and see the five-tab shell in either palette. Every later module plugs into these foundations without reworking them.

---

## 1. Repository structure

```text
shapers/
├─ .gitignore                    created before the first commit
├─ README.md                     how to run everything locally
├─ backend/
│  ├─ Shapers.slnx
│  ├─ Directory.Build.props      nullable, warnings-as-errors, analyzers
│  ├─ Directory.Packages.props   central NuGet versions
│  ├─ src/
│  │  ├─ Host/Shapers.Api        composition root: auth, OpenAPI, module registration
│  │  ├─ BuildingBlocks/
│  │  │  ├─ Shapers.SharedKernel     Entity, AggregateRoot, DomainEvent, Result, ScopeRef
│  │  │  └─ Shapers.Platform         outbox/inbox, audit log, tenancy, permission checks, EF conventions
│  │  └─ Modules/
│  │     ├─ Identity/  Domain · Application · Infrastructure · Api · Contracts
│  │     ├─ People/    Domain · Application · Infrastructure · Api · Contracts
│  │     └─ Church/    Domain · Application · Infrastructure · Api · Contracts
│  └─ tests/
│     ├─ Shapers.ArchitectureTests          enforces module boundaries
│     └─ Modules/<Module>/{Domain,Integration}.Tests
├─ mobile/                        Expo app (Expo Router, Native Tabs)
├─ admin/                         admin portal
├─ packages/                      shared TypeScript (pnpm workspace)
│  ├─ tokens/                     generated theme tokens (TS + CSS)
│  └─ api-client/                 generated from the backend OpenAPI spec
├─ design/
│  ├─ shapers-glass.html          the mockup (moved from the root)
│  ├─ tokens/tokens.json          single source of truth for colours, type, radii
│  └─ brand/                      logo, banner, sample event artwork
├─ docs/
│  ├─ overview.md                 project overview for the team (vision, modules, roadmap)
│  ├─ phase-0-plan.md
│  └─ decisions/                  one short ADR per architecture decision (0001-modular-monolith.md, ...)
├─ infra/
│  └─ docker-compose.yml          Postgres (pgvector image) + Mailpit for local dev
└─ .github/workflows/             CI
```

Why the additions beyond your list:
- **`packages/`** lets mobile and admin share tokens and a typed API client without copy-paste.
- **`infra/`** keeps local and deployment infrastructure out of `backend/`.
- **Five projects per module** gives compiler-enforced boundaries. `Contracts` is the only thing other modules may reference. Architecture tests (NetArchTest) fail the build if, say, Giving references `People.Domain`.
- JS tooling: **pnpm workspaces** for `mobile`, `admin` and `packages/*`. Backend is plain `dotnet`.

---

## 2. Backend foundations

### Conventions
- IDs are **UUIDv7** (`Guid.CreateVersion7()`): sortable, safe to expose, and generated without a database round trip.
- Each module owns a **Postgres schema** (`identity`, `people`, `church`, `platform`) and its own DbContext. No foreign keys cross schemas; cross-module links are IDs kept consistent by events.
- Timestamps are `timestamptz` in UTC. Display uses the campus time zone (default `Africa/Johannesburg`).
- REST + OpenAPI (built into .NET 10). The TypeScript client is generated from it, so the API contract is checked at compile time on the clients.
- Optimistic concurrency via Postgres `xmin` on aggregates.

### Tenancy
Shapers only (single-tenant). There is exactly one Organisation, and it is the root of the scope tree (`shapers`). Campuses and ministries carry `organisation_id`; other records carry a scope path that starts at the organisation. There is no tenant resolution or global query filter. See [ADR 0002](decisions/0002-single-tenant.md).

### Outbox and background jobs
- Aggregates raise domain events. On `SaveChanges`, events that other modules care about become **integration events** written to the module's `outbox_messages` table **in the same transaction** as the data change.
- A background service per module picks up unsent rows with `FOR UPDATE SKIP LOCKED`, publishes them in-process to subscribers, and marks them sent. Subscribers record handled message IDs in an **inbox** table, so a handler that runs twice does no harm.
- **Hangfire** (Postgres storage, `jobs` schema) runs recurring jobs declared by modules: outbox, inbox and credential clean-up now; reminders and statements later. Its dashboard is at `/jobs` behind `platform.jobs.view`. MassTransit only pays off with a broker and several services, which we do not have.

### Audit log (POPIA)
`platform.audit_entries`: actor, action, entity type/id, scope, timestamp, IP, request ID, and a JSON diff. The log is append-only: a database trigger rejects UPDATE, DELETE and TRUNCATE on the table, whoever connects. Writes to Person, grants and consent are always audited. **Reads** of anything marked sensitive are audited too.

---

## 3. Data model

### Church / Campus module
| Entity | Key fields | Notes |
|---|---|---|
| Organisation | id, name, legal_name, pbo_number?, is_18a_approved, default_time_zone, default_currency (ZAR) | the root of the scope tree |
| Campus | id, org_id, name, slug, address, geo, time_zone, status, is_primary | one campus: Rivonia (street address to be confirmed) |
| Ministry | id, org_id, campus_id?, name, slug, parent_ministry_id? | null campus = church-wide (e.g. Kids across all campuses) |

Groups are a V2 module but are also a scope, so the scope model below supports them without Phase 0 knowing anything about groups.

### Scope model (shared by all modules)
Scopes are dot-separated paths. The Church module owns the tree (organisation, campuses, ministries) and exposes it through `IChurchDirectory`:

```text
shapers                                      GLOBAL
shapers.campus_rivonia                       CAMPUS
shapers.campus_rivonia.ministry_kids         MINISTRY
shapers.ministry_worship                     MINISTRY spanning every campus
shapers.campus_rivonia.ministry_kids.group_x GROUP (added in V2)
```

- Every scoped record stores its path in a `scope` column.
- "Does grant G cover record R?" is `R.scope = G.scope OR R.scope LIKE G.scope || '.%'`, which uses a `text_pattern_ops` index. List queries OR together the user's allowed scopes.
- Slugs are fixed when a campus or ministry is created, so renaming never changes a path. See [ADR 0003](decisions/0003-scoped-permissions.md).
- **PERSONAL** is not a tree node. It is an ownership rule: you may always see and edit your own profile and household, subject to field rules.

### Identity module
| Entity | Key fields | Notes |
|---|---|---|
| User | id, person_id (unique), email?, phone (E.164)?, security fields from ASP.NET Identity | always linked to exactly one Person |
| RefreshToken | id, user_id, token_hash, family_id, expires_at, revoked_at, replaced_by, device | rotated on every use; reusing an old token revokes the whole family |
| Permission | key, module, description, is_sensitive | defined in code by each module, synced to the table on startup |
| Role | id, org_id, name, description, is_system | editable bundles of permissions |
| RolePermission | role_id, permission_key | |
| Grant | id, user_id, role_id, scope_node_id, granted_by, granted_at, expires_at?, reason | the actual Role → Permissions → Scope assignment |

Seeded roles: Super Admin (org), Campus Pastor, Campus Administrator, Ministry Leader, Group Leader (V2). Members need no grant; their access is PERSONAL.

Permission keys follow `module.resource.action`, for example `people.profiles.view`, `people.profiles.edit`, `people.profiles.merge`, `people.sensitive.view`, `identity.grants.manage`, `church.campuses.manage`.

### People module
| Entity | Key fields | Notes |
|---|---|---|
| Person | id, org_id, scope_path (home campus), first_name, last_name, preferred_name, date_of_birth?, gender?, membership_status_id, status (active/inactive/deceased/merged), merged_into_id?, source (admin, self-registration, visitor card, event, giving, import), photo_key? | the church record |
| ContactPoint | person_id, type (email/mobile/whatsapp), value (normalised; E.164 for phones), is_primary, is_verified | separate table so dedup can match on it |
| Address | owner (person or household), lines, suburb, city, province, postal_code | |
| Household | id, org_id, scope_path, name, primary_contact_id | |
| HouseholdMember | household_id, person_id, role (adult/child), is_primary | a person can belong to **more than one household** (separated parents matter for kids check-in) |
| MembershipStatus | id, org_id, name, journey_stage (visitor/regular/growth_track/member/...), sort | configurable names, fixed stages for analytics |
| MembershipStatusChange | person_id, from, to, effective_date, changed_by | history, never overwritten |
| ConsentRecord | person_id, purpose, lawful_basis, granted, policy_version, source, recorded_at, withdrawn_at? | append-only POPIA evidence |
| DuplicateCandidate | person_a, person_b, score, reasons, status | produced by a matching job on name + phone/email + DOB |
| PersonMerge | survivor_id, merged_id, merged_by, snapshot (JSON), merged_at | auditable, reversible by hand |

Merging keeps the losing record as a tombstone (`status = merged`, `merged_into_id`) and publishes `PeopleMerged`. Every module that stores a person ID subscribes and re-points its references. Old IDs still resolve, so nothing breaks while that happens.

Deliberately **not** in People: medical notes, allergies and pickup authorisations (Kids module, V2, with tighter rules); giving data; pastoral notes. This keeps special personal information out of the most widely read table.

Custom fields are deferred to V1, once we know what the church actually tracks (see Q6).

The **Shapers Growth Track** (the existing two-week programme) belongs to the Discipleship module as a pathway: enrolment, sessions, attendance, completion. Phase 0 only makes sure People can support it: a `growth_track` journey stage, and a `GrowthTrackCompleted` event that later drives next steps (join a group, start serving).

---

## 4. Permission enforcement
- Endpoints declare what they need: `.RequirePermission(PeoplePermissions.ProfilesView)`.
- For a single record, `IAuthorizer.Can(user, permission, record.ScopePath)` checks the user's grants.
- For lists, `IScopeFilter.PathsFor(user, permission)` returns the allowed scope paths, and EF adds the `<@ ANY(...)` filter to the query.
- A user's effective grants are computed once per request. Redis caching comes later, only if it shows up in measurements.
- Permissions marked sensitive always write an audit entry when used.
- **Tests:** a table-driven suite covering scope containment (global covers campus; campus A does not cover campus B; an expired grant covers nothing; ministry grants do not leak to the parent campus), PERSONAL ownership, and grant management (you cannot grant a permission you do not hold yourself, at a wider scope than your own).

---

## 5. Authentication

Recommendation (**needs your confirmation**, see Q-A below):
- **Self-hosted ASP.NET Core Identity.** A managed provider charges per monthly active user and adds a vendor to the POPIA picture, while phone OTP still needs our own SMS/WhatsApp delivery. Self-hosting keeps the Person ↔ User link and consent handling in one place.
- **Members:** phone number + one-time code as the main login (SMS first, WhatsApp OTP when the WhatsApp Business account exists), with email + password as a fallback. Codes are hashed, short-lived and rate-limited per number and per IP.
- **Staff:** email + password + a second factor (passkey or authenticator app), required for anyone holding a sensitive permission.
- **Mobile tokens:** short-lived JWT access token (about 15 minutes) and a rotating refresh token kept in `expo-secure-store`.
- **Admin portal:** HttpOnly, Secure, SameSite cookie session instead of tokens in browser storage, so injected script cannot steal a token.
- Google/Microsoft sign-in can be added later without schema changes.

---

## 6. Theme tokens (shared by mobile and admin)

One source file, `design/tokens/tokens.json`, in the W3C design-token format. A small build script in `packages/tokens` generates:
- `tokens.ts`: `themes.midnight` and `themes.rose` as typed objects, for React Native.
- `tokens.css`: CSS variables under `:root[data-palette="midnight"]` and `:root[data-palette="rose"]`, for the admin portal.

Tokens are **semantic** (named for the role, not the colour), taken from the mockup:

| Group | Tokens |
|---|---|
| surface | `background`, `screen` |
| glow | `glowPrimary`, `glowSecondary`, `glowAccent`, `edgeGlow`, `rim` |
| glass | `fill`, `fillActive`, `edge`, `highlight`, `sheen`, `shadow` |
| text | `primary`, `secondary`, `tertiary`, `onAccent`, `onButton` |
| accent | `accent` (live badge, Give, progress), `interactive` (links, icons, active tab) |
| button | `primaryTop`, `primaryBottom` |
| misc | `track`, `tile`, `tileEdge`, `video1..3` |
| type | `fontUi` (SF Pro → Figtree), `fontSerif` (Newsreader italic) |
| shape | radii 16/20/24/30, spacing scale |

Rules:
- Components only use `useTheme()` (mobile) or `var(--…)` (admin). An ESLint rule blocks colour literals in component files.
- Each palette also records its system appearance (Midnight = dark, Rose = light). The app sets the native colour scheme to match, so the system tab bar and native glass render the correct way.
- Palette choice: **Automatic** by default (follow the phone's light or dark mode), or pinned to Midnight or Rose. Stored on the device with Zustand and synced to the member's profile so it follows them to a new phone.
- A token test checks text/background contrast against WCAG AA for both palettes.

---

## 7. Member app shell
- Latest Expo SDK, Expo Router, TypeScript strict, React Query, Zustand.
- `app/(tabs)/_layout.tsx` uses **Native Tabs**: Home, Discover, Live, Community, Give, and a **Search** trigger with the system search role. On iOS 26 that renders as the separate circular search button in the Liquid Glass tab bar. Tab icons are SF Symbols on iOS and Material icons on Android.
- `GlassCard` / `GlassButton` wrap `expo-glass-effect`'s `GlassView`. When `isLiquidGlassAvailable()` is false (older iOS, Android), they fall back to a blur view with the glass tokens, or a solid translucent fill if blur is too costly.
- `GlowBackground`: the soft coloured blobs from the mockup, drawn with gradients and a slow drift animation that stops when the system Reduce Motion setting is on.
- Profile is a modal route opened from the avatar on Home.
- Auth screens: phone entry → code → first-time profile completion (creates or links a Person, records consent).
- Phase 0 screens are placeholders built from real components, matching the mockup layout. No real content until V1.
- Other plumbing: typed API client with automatic token refresh, a sensible offline/error state, and `expo-localization` wired in for later translations.

## 8. Admin portal shell
- Sign-in (with 2FA), a sidebar with one entry per module, and a **campus/scope switcher** that shows only what the user's grants allow.
- Working screens in Phase 0: People list/detail (read-only plus basic edit), Campuses, Roles & Grants, Audit log viewer.
- The same tokens, used lightly: glass for the chrome, plain high-contrast surfaces for dense tables.

## 9. CI (GitHub Actions unless you prefer Azure DevOps)
- **backend:** restore, build (warnings as errors), unit tests, integration tests against real Postgres via Testcontainers, architecture tests.
- **web/mobile:** pnpm install, typecheck, lint (including the colour-literal rule), unit tests, admin build, token build check.
- **security:** dependency audit, secret scanning. No secrets in the repo; local config uses `dotnet user-secrets` and `.env.local` (git-ignored).
- EAS builds and Azure deployment are wired up in late Phase 0 or early V1, once hosting is decided.

## 10. Phase 0 tests (minimum)
- Scope containment and grant rules (section 4).
- Refresh-token rotation and reuse detection; OTP expiry and rate limits.
- Person merge (references, tombstone, event), household membership rules, append-only consent.
- Outbox: event saved with the data change, dispatched once, re-dispatch is harmless.
- Architecture tests for module boundaries.
- Token contrast checks.

## 11. Order of work after approval
1. `.gitignore` first (including local tooling files), then `git init`, workspace skeleton, move existing assets into `design/`, docker-compose, CI running on empty projects.
2. SharedKernel + Platform (outbox, audit, tenancy, scope tree).
3. Church module, then Identity (auth + grants), then People.
4. Tokens package.
5. Mobile shell, then admin shell, both against the real API.
6. Seed data: the Shapers organisation, the Rivonia campus, system roles and the first administrator. Development adds example ministries.

Copy and tone across both apps follow the church's own language: "build productive people for the kingdom of God", with a focus on purpose, spiritual growth and personal development.
