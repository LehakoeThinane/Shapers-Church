# 0011. One Communications module for every message

Status: Accepted (2026-09-30)

## Context
Many modules need to tell people things: a service is live, a sermon is out, a seat opened up, a prayer request was approved, the church has news. If each module talked to push, email and SMS providers itself, consent checks, quiet hours, opt-outs and delivery records would be done differently in every place, and some would be missed. Under POPIA, direct marketing needs consent and an easy way to opt out.

## Decision
- **Other modules never contact providers directly.** They publish integration events (`LivestreamStarted`, `SermonPublished`, `WaitlistPromoted`, `PrayerRequestApproved`, ...). Communications decides who hears about each one and how. Event confirmation emails stay in Events for now because they are part of the booking itself.
- **Every notification lands in the member's in-app inbox.** Push and email are extra deliveries on top. Push needs a registered phone, consent to push and the topic switched on. Email needs an address, consent to email and the topic switched on for email.
- **Consent is checked twice:** when a message is queued and again when it is sent, so withdrawing consent stops messages already in the queue.
- **Quiet hours:** from 21:00 to 07:00 (Johannesburg) non-urgent messages wait until morning. Only "we're live" is urgent.
- **Push** goes through Expo's push service, which relays to Firebase (Android) and APNs (iPhone). Phones that report the app as uninstalled are disabled automatically.
- **Announcements** are written by staff for the whole church, a campus or a ministry. Anything wider than a ministry needs a second person to approve it, and the author can't approve their own.
- **Every announcement email carries a signed one-click unsubscribe link.**
- **Personal details never go in a notification.** Phones show notifications on the lock screen, so prayer text, health details and similar stay in the app.
- **A delivery log** records every attempt. Reading it is an audited sensitive read.
- **SMS and WhatsApp** will be added as further channels in the same module once providers are chosen.

## Consequences
- Adding a new trigger is one handler in Communications, and the consent and quiet-hours rules come with it.
- Announcements to everyone create one inbox row per person. That is fine at church scale; bulk email may need a provider's batch API if the audience grows to many thousands.
- Push can only be tested in our own app build, not in Expo Go.
