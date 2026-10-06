# 0019. Services: worship planning, teams and scheduling

Status: Accepted (2026-10-06)

## Context
The pastor asked for something like Planning Center Services. Every week, someone builds the order of service, chooses songs, and phones or WhatsApps the band and production team to ask who can serve. Shapers holds a CCLI licence, so lyrics may be stored and shown, and song use must be reported.

## Decision
- **Services module** (schema `services`) with:
  - teams, positions and team members;
  - service types (templates);
  - plans;
  - assignments (who is asked to serve where, and their answer);
  - blockout dates;
  - songs with arrangements.
- **Permissions:**
  - `services.plans.edit`: build plans and templates, and run the live run sheet.
  - `services.schedule`: manage teams and schedule people, at the team's scope.
  - `services.songs.edit`: keep the song library.

  Campus pastors hold all three. Two new built-in roles: **Worship leader** (all three) and **Serving team leader** (scheduling).
- **Plans** hold their order of service and position needs as JSON on the plan row (always read together). Start times are worked out from the lengths. A plan starts as a copy of its template.
- **Scheduling** only offers people who are on the team in that position. Candidates show who is away, who is already serving that day, and when each person last served, so serving is shared out. Safeguarding:
  - under-18s can join only teams marked open to minors;
  - an under-18 can be scheduled only once an adult from the same team is on that plan.
- **Answers:** people answer in the app (*My schedule*) or from the email.
  - The email links to a page signed with the API's data protection keys (valid 90 days). Opening it never answers; only its buttons do, so link scanners can't answer for anyone.
  - That one path is exempt from the CSRF header rule because the signed token, not a cookie, authorises it.
  - Requests, reminders (three days before) and "can't serve" alerts to the team's schedulers go through Communications as pushes, under a new **Serving** topic. Services sends the email with the answer links itself, as transactional email.
- **Away dates** block scheduling on those days. Dates more than 30 days past are deleted.
- **Songs:** CCLI number, themes, lyrics, a reference recording, and arrangements with key, tempo, chord chart and rehearsal recording. Files use the existing media storage; song editors may upload. Usage (last used, times used, and the CCLI report with a CSV download) is worked out from the plans.
- **Live run sheet:** the plan stores the current item and when it started. Planners move it on; staff and everyone serving on the plan follow it by polling every two seconds.
- **Music stand:** the plan's songs full screen in the admin portal, with the chord chart (PDF or image) or the lyrics. Public media files may be framed by the portal (`X-Frame-Options` is relaxed for `/media-files` only, and the portal's CSP allows the storage host).
- **Personal data:** team membership, serving history and away dates are included in data export and erasure, and follow a person when records are merged.

## Consequences
- Volunteers need the app, or an email address on their church record, to be asked. SMS and WhatsApp requests wait for a provider.
- Serving history is deleted after two years (approved 2026-10-06), and away dates a month after they pass. Both are in the privacy notice.
- The live run sheet uses polling, not SignalR, which is enough for one church's screens. It can move to the existing hub if needed.
