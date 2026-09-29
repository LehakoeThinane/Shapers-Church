# 0010. Event registration: seat locking, guest email codes and tickets

Status: Accepted (2026-09-29)

## Context
Popular events (conferences, marriage workshops, kids' camps) fill up in minutes. Two people booking the last seat at the same moment must not both get it. Visitors who don't have the app need to register from the website, without us creating accounts for them or accepting fake addresses. Tickets have to open at the door even when the venue has poor signal.

## Decision
- **Seats are allocated under a row lock.** Each booking or cancellation runs in one transaction that takes `SELECT … FOR UPDATE` on the event row, re-reads the event and its counts, then decides. Bookings for one event queue behind each other. Bookings for different events don't block each other.
- **The waitlist is strict first-in, first-out.** A new booking can't jump the queue while people are waiting. When seats free up, the waitlist promotes in order and stops at the first party that doesn't fit.
- **Members register in the app**, for themselves and for people in their household.
- **Guests register on the website with an emailed code.** The code has 6 digits, lasts 15 minutes, allows 5 attempts and works once, and at most 3 codes go to one address per 15 minutes. A verified guest becomes a Person (source: event registration) with a consent record. They get a private link to view or cancel. Only a keyed hash of the link key is stored.
- **Staff can book people who phone in.** A new person needs their verbal consent, which staff record.
- **Tickets are 10-character codes** from an alphabet with no look-alike characters, shown as QR codes prefixed `SHAPERS-T:`. The app keeps the latest tickets on the phone. Check-in works by scan, typed code or name search.
- **Door volunteers see names only.** Attendee details and the CSV export need `events.registrations.view`, which counts as a sensitive read and is audited. The export neutralises spreadsheet formulas.
- **Transactional email** goes through Azure Communication Services with the Africa data location (see ADR 0009). Email failures are logged. They never undo a booking.

## Consequences
- Overselling is prevented by the database, not by application timing. An integration test fires simultaneous bookings at the last seats.
- Row locks serialise bookings per event. That is fine at church scale. If one event ever needs thousands of bookings per second, revisit this.
- SMS and WhatsApp confirmations come later through the Communications module.
