# Shapers Church Platform: Overview

## Vision

Our own equivalent of Planning Center and Church Center, built to grow with Shapers Church: more members, more ministries and, one day, more campuses.

- **Admin portal**: staff do almost all administration here.
- **Member app and public website**: members watch, give, register, join groups, request prayer and see their serving schedule.
- **One backend platform.** The app is a window into the system, not the system. Group leaders get light admin in the app (for example, taking group attendance), and Sunday check-in may get dedicated device screens later.

Shapers exists to "build productive people for the kingdom of God", with a focus on purpose, spiritual growth and personal development. That language shapes the product's copy and tone. The existing **Shapers Growth Track**, a two-week programme for new and growing members, becomes a trackable pathway in the Discipleship module: enrolment, sessions and completion, leading to next steps such as joining a group or serving.

Church contact: info@shaperschurch.com. One campus: 8 Mellis Road, Rivonia, Sandton, Johannesburg.

## Architecture

```text
   Member app (iOS/Android)     Admin portal (web)     Public website
                 └──────────────────────┼──────────────────────┘
                          HTTP API (auth, rate limits)
                                        │
                         Modular monolith (.NET 10)
                                        │
          PostgreSQL · object storage · background jobs (Hangfire)
                                        │
  Integrations: streaming, SA payment gateway, push, email/SMS, WhatsApp,
                maps, Bible API, accounting, calendar
```

Principles:

- **Modular monolith, not microservices.** Each module has Domain, Application, Infrastructure and Api layers, plus a public Contracts project. Modules talk only through Contracts: integration events and query interfaces.
- **Transactional outbox.** A side effect (an email, another module's update) is published only if the change that caused it was saved, and delivered at least once.
- **Payment provider webhooks are authoritative.** The app never decides that a payment succeeded.
- **Scoped permissions.** A grant gives a role (a set of permissions) at a scope: the whole church, a campus, a ministry, later a group. Code asks for a permission at a scope and never checks role names.
- **Person is not User.** A person is a church record; a login is optional. Households, duplicates and merges are first-class.
- **Prayer and Pastoral Care are separate modules** with different confidentiality rules.

## Modules

| Module | Purpose | Phase |
|---|---|---|
| Identity | Logins, sessions, roles, permission grants | 0 |
| People | The church database: people, households, membership journey, consent, duplicates and merges. Every other module reads from and writes back to People. | 0 |
| Church | The organisation, campuses and ministries; owns the scope tree | 0 |
| Sermons, Media and CMS | Sermons, series, speakers, audio and video, notes, livestream | V1 |
| Events | Calendar, registration, capacity, waiting lists, payments, tickets | V1 |
| Giving | Once-off and recurring giving, funds, ledger, reconciliation, Section 18A certificates | V1 |
| Prayer | Requests visible publicly, to a group, to pastors only, or anonymously | V1 |
| Communications | Push, email, SMS and WhatsApp with audience targeting | V1 |
| Groups | Home cells: leaders, meeting reports, teaching and church lessons (built); other groups and safeguarded group chat later | V2 |
| Services and Volunteers | Service planning, rosters, availability | V2 |
| Kids Check-In | Pre-check-in, security codes, labels, authorised pickup, offline mode | V2 |
| Calendar and Resources | Room and resource booking | V2 |
| Ticketing, Forms, Bible and Discipleship | Tickets, custom forms, reading plans, courses, Growth Track | V2 |
| Assist (AI help) | Sermon transcripts, AI drafts of show notes, cell lessons and rewrites for staff to review; monthly budget (built) | V3 |
| Search, Analytics, Automation | Semantic search, aggregate engagement metrics | V3 |

## South African requirements

- **POPIA.** Church membership reveals religious belief, and children's medical notes are health data: both are special personal information. The platform records consent and its lawful basis, keeps data minimal, audits access to sensitive records, and supports correction and deletion requests. The church needs a registered Information Officer.
- **Section 18A.** If Shapers is an approved PBO, Giving issues 18A certificates.
- **How people give.** EFT with references and bank reconciliation, SnapScan and Zapper, DebiCheck debit orders, and cards through hosted payment pages.
- **Data costs.** Audio first, offline downloads, a low-bandwidth mode, small app bundles.
- **WhatsApp** is a primary channel. Content will be multilingual.

## Licensing and safeguarding

- Modern Bible translations need licences; KJV and WEB don't. Local-language translations may need permission from the Bible Society of South Africa.
- Streaming worship and showing lyrics need streaming and lyric licences (e.g. CCLI).
- Apple has specific rules for donations inside apps; confirm eligibility before building in-app giving.
- No private one-to-one messaging between adults and minors; leaders are visible in youth chats; reporting and moderation exist from day one; kids check-in has strict access control and an offline, manual fallback.

## Design

The member app uses iOS 26 Liquid Glass: the real system tab bar (Expo Router native tabs) and native glass views, with graceful fallbacks on older iOS and on Android. Members switch between two palettes, **Midnight** (dark, gold accents) and **Rose** (light, rose and taupe). Colours live only in `design/tokens/tokens.json`. The reference mockup is `design/shapers-glass.html`.

Tabs: Home · Discover · Live · Community · Give, plus a separate Search button. Profile is the avatar on Home.

## Roadmap

- **Phase 0, Foundation:** repository, Identity, People and Church modules, scoped permissions, outbox, theme tokens, app and admin shells, CI.
- **V1, Core church:** profiles and households, Home, sermons and media, livestream, events, giving, prayer, notifications, admin content management, consent flows.
- **V2, Community:** groups and safeguarded messaging, service planning, volunteers, kids check-in, calendar and resources, ticketing, Bible and reading plans, forms.
- **V3, Intelligence:** sermon processing with human approval, semantic search, personalised content, pastoral workflow automation, governed analytics.

## Open questions

1. Ticketing: event tickets, support tickets, or both?
2. Payment gateway, and is Shapers an 18A-approved PBO?
3. Congregation size.
4. Existing membership data to migrate, if any.
