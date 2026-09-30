# V1 Plan: Sermons and Media, Livestream, Events

Status: **Approved (2026-09-29).** Decisions are recorded at the end. Slices A, B and C are built. Slice C sends its own transactional emails (guest codes, confirmations, waitlist, cancellations and reminders) because guest registration needs email codes; see [ADR 0010](decisions/0010-event-registration.md). Other member notifications still wait for slice D.

This is the first part of V1. It adds two modules, **Media** (sermons, series, speakers, audio, podcast, livestream) and **Events** (events, registration, waiting lists, tickets, check-in), and the screens that use them in the admin portal and the member app. Giving, Prayer and Notifications get their own plans.

## Where we start from

- Sermons today are **YouTube videos only**: about 40 on the website, titles only, with no speaker, date, series, scripture, audio or podcast.
- Events are promoted with posters and a phone number (for example EPA 2026: "register on 079 711 3615"). There is no online registration.
- Phase 0 gives us: people and households, scoped permissions, the outbox, background jobs, audit, the two apps and the design system.

## Goals

1. A member can find any sermon by series, speaker, topic or scripture, and **listen to it as audio** on a small data bundle, including offline.
2. Sunday's service is watchable live in the app, with notes, the scripture on screen, a connect card and a Give button.
3. Anyone, member or visitor, can **register for an event in under a minute**. Staff see who's coming, manage capacity and waiting lists, and scan tickets at the door.
4. Every piece is usable on its own, and each is shipped as a slice.

## Delivery slices

| Slice | What ships | Rough size |
|---|---|---|
| **A. Sermon library** | Speakers, series, sermons; YouTube video; audio upload; publish and schedule; admin editor; app Discover tab with audio player and downloads; podcast feed; import of the existing YouTube sermons | Largest |
| **B. Livestream** | Service streams, live status, Live tab (video, notes, scripture, Give, connect card), admin live control, recording becomes a sermon | Medium |
| **C. Events** | Events, registration for members and guests, capacity, waiting list, QR tickets, door check-in, attendee lists and export | Large |
| D. Notifications (separate plan) | Push, email, SMS and WhatsApp delivery for confirmations, reminders, "we're live", new sermons | Medium |

A to C only *publish* events such as `SermonPublished`, `LivestreamStarted`, `EventRegistered` and `WaitlistPromoted`. Nothing is sent to members until slice D consumes them. This keeps A to C unblocked by the choice of SMS, email and WhatsApp providers.

---

## Slice A: Sermon library

### Data model (`media` schema)

| Entity | Key fields | Notes |
|---|---|---|
| Speaker | id, name, title ("Senior Pastor"), bio, photo, person_id? | Link to a Person when the speaker is on staff; guest speakers need no record |
| Series | id, title, slug, description, artwork, starts_on, ends_on, scope | e.g. "The servant songs of Isaiah" |
| Sermon | id, title, slug, series_id?, preached_on, scope, summary, notes (Markdown), status (Draft, Scheduled, Published, Archived), publish_at, language, topics[] | Scope is church-wide or a campus |
| SermonSpeaker | sermon_id, speaker_id, order | Panels and shared sermons |
| ScriptureReference | sermon_id, book, chapter_from, verse_from, chapter_to, verse_to, order | Parsed from text such as "Psalm 42:1-11" |
| MediaAsset | id, kind (Audio, NotesPdf, Artwork), storage_key, content_type, size_bytes, duration_seconds?, checksum | Files live in object storage, not the database |
| VideoLink | sermon_id, provider (YouTube), external_id | Video stays on YouTube: we never host or transcode it |
| PlaybackPosition | person_id, sermon_id, position_seconds, updated_at | Powers "continue watching"; see privacy below |

Rules:
- A sermon can be published only with a title, date, at least one speaker, and a video or an audio file.
- Scheduling publishes it at `publish_at` (a Hangfire job) and raises `SermonPublished`.
- Slugs are stable, so shared links keep working after a title is edited.

### Files and data costs
- A `IFileStorage` abstraction over **Azure Blob Storage in South Africa North** (Johannesburg). Keeping members' data in the country is the simplest POPIA position. Local development uses the file system.
- Staff upload directly to storage with a short-lived signed URL, so large files don't pass through the API.
- **Audio is the primary format.** We ask for an MP3 or M4A from the sound desk or the recording, and keep it at about 64 kbps mono, roughly 28 MB for an hour. Public files are served through Cloudflare's CDN.
- We do **not** download audio from YouTube. It breaks YouTube's terms, so audio has to come from the church's own recording (see question 1).
- Offline listening: the app downloads audio to the device and marks downloaded sermons. A **low-data mode** setting prefers audio, stops autoplay, and uses small artwork.

### Podcast
A public RSS feed (`/podcast.xml`) built from published sermons that have audio. It can be submitted to Apple Podcasts and Spotify once, and updates itself after that.

### Importing today's sermons
A one-off admin action reads the church's YouTube channel or playlist (YouTube Data API, read-only key) and creates **draft** sermons with the title, date and video. Scripture is parsed from titles where possible, e.g. "Psalm 42: 1-11 Deep calls unto deep". Staff add speakers and series, then publish.

### Search
PostgreSQL full-text search over title, summary, speakers, series and scripture, with filters for series, speaker, book of the Bible, topic and year. Semantic search stays in V3.

### Privacy
Sermon playback history is personal information, and in a church app it hints at belief. We keep only the last position per sermon, delete positions older than six months, and never use them for per-person analytics. Aggregate play counts are fine.

### Permissions
`media.sermons.edit` (create and edit drafts), `media.sermons.publish` (publish, schedule, archive), `media.speakers.manage`. Scoped like everything else: a campus editor can publish campus sermons, not church-wide ones.

### Admin portal
- Sermons list with status filters.
- Sermon editor: details, speakers, series, scripture, topics, Markdown notes with preview, YouTube link with preview, audio upload with progress, and publish or schedule.
- Series and speaker screens, and the YouTube import screen.

### Member app
- **Discover** tab: latest sermon, current series, series grid, and a search entry point.
- **Sermon screen**: YouTube player (embedded) or audio player, notes, scripture references, share, download.
- **Audio player**: background playback with lock-screen controls (expo-audio), speed control, and a mini-player in the native tab bar's bottom accessory on iOS 26.
- Home's **Continue watching** becomes real.

---

## Slice B: Livestream

### Model
| Entity | Key fields |
|---|---|
| Livestream | id, title, scope, scheduled_start, status (Scheduled, Live, Ended), youtube_video_id, notes (Markdown outline), give_url?, sermon_id? |
| ScriptureCue | livestream_id, reference, text, shown_at, order: what's "on screen" right now |
| ConnectCard | id, livestream_id?, person_id?, name, phone/email, reasons (first time, decision, prayer, info), message, handled_by, handled_at |

### How it works
- Staff create the Sunday stream in advance with its YouTube video ID. YouTube Live is free and already familiar.
- A producer presses **Go live** in the admin portal, which raises `LivestreamStarted`. We don't poll YouTube for status in V1: a manual control is reliable and costs no API quota.
- During the service the producer taps the next scripture, and the app shows it under the video. The app checks live state every 15 seconds with a cheap cached request. Real-time sockets come with live chat.
- After the service, **Make this a sermon** turns the stream into a draft sermon with the video and notes filled in.
- **Connect card** (visitors, decisions, prayer) creates or matches a Person, using the same dedup rules as sign-up, and lands in a staff follow-up list. Prayer requests are kept for the Prayer module, not mixed into People.

### Live chat (see question 4)
The design shows moderated live chat. Chat needs real-time delivery, moderation tools, reporting and message retention from day one. Doing that properly is about as much work as the rest of slice B, so I suggest shipping B without chat and adding it straight after as B2.

### Member app
The Live tab gets:
- the video, with a "Starts in 20 min" state before the service
- a Notes / Bible / Prayer / Connect switcher
- the scripture on screen
- a Give button, which links to the current giving page until V1 Giving lands

---

## Slice C: Events

### Model (`events` schema)
| Entity | Key fields | Notes |
|---|---|---|
| Event | id, title, slug, summary, description, scope, starts_at, ends_at, time_zone, location (campus or custom address), image, visibility (Public, Members, Scope), status (Draft, Published, Cancelled) | Multi-day events (e.g. the 3–5 September conference) are one event |
| RegistrationSettings | event_id, required, opens_at, closes_at, capacity?, waitlist_enabled, max_per_registration, questions (up to 5 simple fields) | Custom forms arrive with the Forms module in V2 |
| Registration | id, event_id, registrant_person_id, status (Confirmed, Waitlisted, Cancelled), created_at, source (App, Web, Admin), answers | One registration can include household members |
| Attendee | registration_id, person_id, ticket_code, checked_in_at, checked_in_by | Each attendee has their own QR code |

### Rules (unit-tested, as they affect people turning up at the door)
- Seats taken is the number of confirmed attendees. A registration that would go over capacity joins the waiting list.
- A cancellation releases seats, and the waiting list is promoted in order. Promotion is atomic, so two cancellations can't promote the same person twice (row locking in PostgreSQL).
- Registration opens and closes on time. A cancelled event cancels its registrations and notifies attendees in slice D.
- Ticket codes are random, unguessable and single-use at the door. Scanning twice shows "already checked in at 09:42".

### Members and guests
- **Members** register from the app in two taps, and can add household members.
- **Guests** register with name, mobile and email plus consent. That finds or creates a Person with the same dedup rules as sign-up, marked as source "event registration". Guests are protected by rate limiting and a verification code by SMS or email (see question 3).

### Staff
- Event editor, and attendee list with search, status and export to CSV. Exports are audited, since attendance is personal information.
- Manual registration, for phone-ins like EPA's.
- **Door check-in**: a scanner page in the admin portal uses the device camera, so any phone or tablet works, with a search fallback by name. Offline check-in is the Kids Check-In module's job (V2).

### Paid events
Paid registration needs the payment gateway. Until one is chosen, events are **free only**, and paid events show "pay at the door". The model already has room for a price and a payment reference, so adding payment later needs no migration of existing registrations.

### Permissions
`events.edit`, `events.publish`, `events.registrations.view` (sensitive), `events.registrations.manage`, `events.checkin`. Door volunteers get only `events.checkin` at their campus.

### Member app
- Upcoming events on Home and Discover.
- Event page with register or join waiting list.
- My tickets, with QR codes shown offline from a local copy.
- Add to calendar.

---

## Cross-cutting

- **Public pages**: visitors will open event and sermon links from WhatsApp, often without the app installed. See question 5.
- **Audit**: publishing, event changes, registration exports and check-ins are audited.
- **Accessibility**: the audio player and QR screens work with the screen reader and large text.
- **Tests**: domain tests for publishing rules, scripture parsing, capacity and waiting-list promotion, and ticket validation. Integration tests for register, cancel, promote and check-in, and for scoped access to attendee lists.
- **ADRs**: file storage and data residency; audio-first media; events capacity and concurrency.

## Suggested order and checkpoints

1. Media foundation: storage, sermons and speakers, admin editor. *Checkpoint: staff can publish a sermon with audio.*
2. App: Discover, sermon screen, audio player, downloads, podcast feed, YouTube import.
3. Livestream (B), then live chat (B2) if you agree.
4. Events (C): model and rules, admin, member registration, guest registration, door check-in.
5. Notifications plan (D).

---

## Decisions (2026-09-29)

1. **Audio:** staff upload an audio file in the admin portal after each service. The media team is confirming whether desk or encoder recordings are available. Automated capture is a later improvement.
2. **Bible:** show references, plus World English Bible (WEB) text where verse text is shown. The preaching translation is to be confirmed; licensing comes later.
3. **Guest registration:** yes. Email verification codes first, SMS and WhatsApp once a provider is chosen. A guest record can later gain a login without losing its history.
4. **Livestream first, then moderated chat** as a follow-up slice.
5. **Public site with Astro**, built to become the new shaperschurch.com over time.
6. **YouTube channel:** https://www.youtube.com/channel/UCZf66xLSk4RyXbMzI_lVf-g. The Google account behind it should belong to the church (e.g. info@shaperschurch.com), with staff added as managers, not a volunteer's personal account.
7. **Hosting:** Azure South Africa North. See [ADR 0009](decisions/0009-hosting-region.md) for the service availability check.
