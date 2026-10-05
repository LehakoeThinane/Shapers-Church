# V1 plan, part 2: finishing Core church

Status: **Approved (2026-09-30).** Decisions are recorded at the end. Slice D (Prayer) is built. Slice E (Notifications) is built except SMS and WhatsApp, which wait for providers; see [ADR 0011](decisions/0011-communications.md). Slice F (POPIA) is built with the retention periods approved on 2026-09-30; see [ADR 0012](decisions/0012-privacy-rights-and-retention.md). Slice G (content and the public website) is built; hosting is Azure Static Web Apps behind Cloudflare (decided 2026-09-30); see [ADR 0013](decisions/0013-public-website.md). Slice B2 (live chat) is built; see [ADR 0014](decisions/0014-live-chat.md).

## Where we are

V1 has ten items. Sermons and media, livestream, and events are done. Profiles and households are done, and Home is mostly done. Four items are left and two are partly done:

| V1 item | State | Slice below |
|---|---|---|
| Prayer | Not started (prayer is only a connect-card reason) | D |
| Notifications | Email for events only | E |
| POPIA consent flows | Consent records and audit log done; data rights, retention and breach handling missing | F |
| Admin CMS | Sermons, livestreams and events editable; no pages, news or blog | G |
| Giving | Not started | H |
| Home | Prayer, Groups and Serve buttons lead nowhere | D (Prayer); Groups and Serve are V2 |
| Livestream | Moderated chat outstanding | B2, after E |
| Events | Paid events outstanding | H |

## Delivery slices

| Slice | What ships | Size | Blocked on |
|---|---|---|---|
| D. Prayer | Prayer requests with visibility levels, a moderated prayer wall, "I prayed", answered prayers | Small–medium | Nothing |
| E. Notifications | Communications module: push, email, SMS; in-app inbox; preferences; staff announcements | Medium–large | Push needs a development build (see decision 2); SMS needs a provider |
| F. POPIA completion | Download my data, deletion and correction requests, retention jobs, privacy notice versions, Information Officer tools | Medium | Leadership sign-off on retention periods |
| G. Content and public website | Pages, news and blog in the admin CMS; the Astro website that replaces shaperschurch.com | Large | Hosting choice for the site |
| H. Giving | Funds, ledger, EFT references and bank reconciliation, statements, receipts and 18A certificates; then online payments | Large | Payment gateway and 18A status |
| B2. Live chat | Moderated live chat during services | Medium | Needs E (reporting alerts) |

Suggested order: **D → E → F → G**, with **H** starting as soon as the gateway and 18A questions are answered. Part 1 of Giving (below) doesn't need the gateway, so it can start earlier if you prefer.

## Slice D: Prayer

A separate module from Pastoral Care (V3), as the brief requires.

| Entity | Key fields | Notes |
|---|---|---|
| PrayerRequest | id, person_id?, name shown, text, visibility, status, anonymous, scope, created_at, answered_at?, answer_note? | Guests can submit through a connect card or the website |
| PrayerResponse | request_id, person_id, prayed_at | Powers "12 people prayed"; one per person |

**Visibility levels:**
- **Public wall:** members see it in the app after a staff member approves it.
- **Pastors only:** only people with `prayer.requests.view` see it. Every read is audited.
- **Anonymous:** the name is hidden everywhere except to pastors, and staff can't reveal it on the wall.
- **Group:** arrives with Groups in V2.

**Rules:**
- Public requests are moderated before they appear. Staff can edit out names of third parties and health details before approving.
- The requester can mark a request answered and add a praise note.
- Requests leave the wall after 30 days. They are deleted after the retention period (slice F).
- Prayer reveals religious belief and often health information, so submitting needs consent. The text is never included in notifications, only "a new prayer request".
- Connect cards with the prayer reason become prayer requests (pastors only by default).

**Screens:**
- App: a Prayer screen from Home's quick action and the Live tab (submit, the wall, "I prayed", my requests).
- Admin: a moderation queue and the pastors-only list.

## Slice E: Notifications (Communications module)

One module that every other module asks to send things. Nobody else talks to providers directly.

- **Channels:**
  - **Push:** through Expo's push service, which relays to FCM and APNs.
  - **Email:** Azure Communication Services, as today.
  - **SMS:** a South African provider, chosen in decision 3.
  - **WhatsApp:** later, once the business account and message templates are approved (decision 4). The design leaves room for it.
- **Preferences:** a member chooses channels per topic (services and live, sermons, events, prayer, announcements). These are checked against their consent records: no consent, no message. Quiet hours run 21:00–07:00, except for time-critical messages such as "we're live".
- **Audience targeting:** everyone, a campus, a ministry, event registrants, or a list of people. Group and age targeting arrive with Groups in V2.
- **Staff announcements:** composed in the admin portal. Anything sent to more than one ministry needs a second person to approve it (`communications.announcements.approve`).
- **In-app inbox:** every notification also lands in an inbox in the app, so nothing is lost if push is off.
- **Triggers wired in:**
  - "We're live" and "new sermon".
  - Event confirmations and reminders (moved from Events).
  - Waiting-list promotions.
  - Prayer moderation, and a follow-up when a request is answered.
- **Delivery log:** every message and its status is recorded, with retention per slice F. Failed sends retry through the outbox.
- **Cost controls:** a per-channel monthly budget. SMS is used only when push and email aren't available.

## Slice F: POPIA completion

- **Download my data:** the member requests it in the app. A job builds a JSON and PDF bundle, and a link valid for 24 hours goes to their verified email.
- **Correction and deletion requests:**
  - Members submit them; the Information Officer's queue records the decision and when it was made.
  - Deletion removes or anonymises the person across modules. Each module handles a `PersonErasureRequested` event.
  - Where the law requires records to be kept, they are anonymised rather than deleted. Giving records must be kept for 5 years for SARS.
- **Retention jobs:** nightly deletion per data type. Proposed defaults, for leadership to confirm:
  - Prayer requests: 1 year.
  - Connect cards: 2 years.
  - Playback history: 1 year.
  - Notification log: 1 year.
  - Unverified guest records: 90 days.
  - Audit log: 5 years.
- **Privacy notice versions:** a new version asks members to review it on next launch. Consent records already store the version.
- **Information Officer tools:**
  - A breach register: what happened, when it was discovered, who is affected, and when the Regulator and people were notified.
  - A data-requests queue.
  - A report of who read sensitive records.
- **Outside the code:** the church must register its Information Officer with the Information Regulator. I'll draft the privacy notice text for your review, but it needs checking by someone qualified.

## Slice G: Content and the public website

**Admin CMS:**
- **Pages:** About, Growth Track, Contact and Giving information.
- **News and announcements:** these appear on Home and on the website.
- **Blog posts:** written in Markdown, with a cover image, scheduled publishing, and a draft → published workflow with an audit trail.

**Public website (Astro, becomes shaperschurch.com):**
- Home, sermons and series, events with guest registration and the guest ticket page that confirmation emails already link to, the livestream, blog, pages, contact (a connect card), and giving information.
- **Static and fast:** pages are rebuilt when content is published, which suits data costs and search engines.
- **Migration:** a one-off import of the existing WordPress blog posts and pages through WordPress's own API. Old URLs redirect to the new ones.
- **Personal data:** the site only reads public content through the API. Forms post to the same rate-limited endpoints the app uses.

## Slice H: Giving

Money and 18A certificates make this expensive to get wrong, so it gets its own detailed plan before building. The outline:

- **Part 1, no gateway needed:**
  - Funds (tithe, building, missions) and campaigns.
  - A double-entry ledger.
  - EFT giving with a personal reference per giver, and bank statement import (Standard Bank CSV) that matches deposits to people. Staff resolve anything unmatched.
  - Recording cash and cheque batches.
  - Giving statements.
  - 18A certificates, if Shapers is an approved public benefit organisation (PBO). This needs the church's PBO number and the SARS-required fields.
- **Part 2, with the gateway:**
  - Card and instant-EFT giving through hosted payment pages.
  - Recurring giving through DebiCheck debit orders.
  - SnapScan and Zapper QR codes.
  - Paid events.
  - Webhooks are authoritative.
  - Apple's in-app donation rules are checked before the iOS flow is final.

## Slice B2: Live chat

Status: **Approved and built (2026-09-30).** A chat beside the video during a livestream, in the app and on the website.

**Who can do what:**
- Everyone watching can read the chat. Posting needs a signed-in member with a verified phone number, so every message is traceable to a person.
- Names show as first name and last initial ("Thabo M."). Staff and moderators carry a badge.
- There are no private messages of any kind. The chat is one public room, so the adult–minor rule is met by design.
- Members under 18 (by date of birth, where we have it) can read but not post in V1. Youth-safe chat arrives with Groups in V2.

**Moderation (`media.chat.moderate`, a new "Chat moderator" role; campus pastors and the media team also hold it):**
- Moderators can hide a message, time a person out for the rest of the service, or ban them from chat. Every action is audited and can be undone.
- Members can report a message. A report alerts the moderators on duty through their inbox and push (slice E). Three reports from different people hide a message until a moderator looks at it.
- Slow mode: one message every 5 seconds per person by default; moderators can raise it. Messages are limited to 300 characters. Links are shown as plain text.
- A word list holds matching messages for a moderator instead of showing them.
- Moderators can switch the chat to "moderators approve every message" if a service attracts trouble.

**Privacy:**
- Before a member's first message, a note says the chat is public and suggests the prayer form for anything personal. A "Pray for me" button beside the chat opens it.
- Chat opens 15 minutes before a service goes live and closes 30 minutes after it ends. Old chats aren't shown to members afterwards.
- Retention: see decision 2 below. Erasure requests delete a person's messages.

**How it works:**
- Real time through ASP.NET Core SignalR (built in, WebSockets with fallbacks). One server is enough for now. If we run more than one API instance, we add Azure SignalR Service; no code changes are needed.
- Messages are saved before they are broadcast, so the moderator view and reports always match what people saw.
- It lives in the Media module next to livestreams, with its own tables.
- On the website, the chat is read-only for guests with a "Sign in in the app to join" prompt, until website sign-in exists.

**Screens:**
- App: a chat panel under the video on the Live tab.
- Website: the same panel on /live.
- Admin: a moderator console showing the live chat, the report queue, held messages and the people timed out or banned.

**Tests:** posting rules (verified, over 18, not timed out or banned, slow mode), moderation actions and audit, the report threshold, the word list, open and close times, retention and erasure.

### B2 decisions (2026-09-30)
1. **Under-18s** read the chat but can't post in V1.
2. **Chat retention:** ordinary messages 90 days; hidden, held or reported messages and moderation actions 1 year.
3. **Website visitors** can read the chat; joining in needs the app.

## Cross-cutting

- **Tests:** tests for every visibility rule, consent check, retention job and ledger posting. The ledger gets its own tests for every money path.
- **New permissions:**
  - `prayer.requests.view` and `prayer.requests.moderate`.
  - `communications.announcements.send` and `communications.announcements.approve`.
  - `privacy.requests.manage` and `privacy.breaches.manage`.
  - `content.pages.edit`.
  - `giving.*`, detailed in the Giving plan.
- **ADRs:** one each for the Communications module and channel fallback, the erasure strategy, and the ledger.

## Decisions needed

1. **Order.** Recommended: D (Prayer) → E (Notifications) → F (POPIA) → G (Content and website), and H as soon as it's unblocked. Or start Giving part 1 now?
2. **Development build for push.** Expo Go on Android no longer supports push notifications. Testing them needs our own development build of the app (a free EAS account) and a Firebase project owned by the church's Google account. This build is also what we'd eventually ship to the stores. OK to set it up?
3. **SMS provider.** Recommended: a South African provider (for example BulkSMS or Clickatell) for local delivery rates and POPIA. Push and email come first, so this can wait until slice E is underway.
4. **WhatsApp.** It needs a verified Meta Business account for the church, a dedicated phone number and approved message templates. That takes weeks, so the church should start the application now if WhatsApp is wanted in V1.
5. **Prayer wall moderation.** Recommended: every public request is approved by staff before it appears. Who does this: the pastors, or a prayer team role?
6. **Retention periods** in slice F. OK as proposed, pending leadership sign-off?
7. **Website hosting.** The site holds only public content. Recommended: Azure Static Web Apps behind Cloudflare. This is allowed by ADR 0009, which keeps personal data in South Africa North and lets public content sit on a CDN.
8. **Giving.** Which payment gateway, and is Shapers an 18A-approved PBO? Its PBO number is needed if so.

## Decisions (2026-09-30)

1. **Order:** D (Prayer) → E (Notifications) → F (POPIA) → G (Content and website). Giving starts once the payment gateway is chosen.
2. **Shapers is an 18A-approved PBO.** Giving must issue 18A certificates; the PBO number is needed before slice H.
3. **Prayer moderation** is a permission (`prayer.requests.moderate`), granted to the Pastor roles by default and assignable to a prayer team role.
4. Still open: the development build for push, the SMS provider, WhatsApp, retention periods, website hosting and the payment gateway. They are asked again when their slice starts.
