# Plan: Services (worship planning, teams and scheduling)

Status: built (2026-10-06): all five parts, the church holds a CCLI licence (ADR 0019). The pastor asked for something like Planning Center Services. It fills the "Services" and "Music stand" products already shown as coming soon in the admin portal.

## What it does, in Planning Center's terms

| Planning Center | Here | Who uses it |
|---|---|---|
| Plans and the order of service | **Service plans:** each Sunday (or event) has an order of service: items with a length, notes, the person leading, and songs. Headers group items (Worship, Word, Response). Start times are worked out from the lengths. Plans are made from a **template** ("Sunday 09:00"), so most of each week is ready. | Pastors, worship leader, service producer |
| Songs and arrangements | **Song library:** title, author, CCLI song number, themes, arrangements with key and BPM, chord chart and lyrics files (PDF), and a YouTube or audio reference. The library shows when each song was last used, to avoid over-using songs. | Worship leader |
| Teams, positions and scheduling | **Teams:** Worship (vocals, keys, guitar, bass, drums), Production (sound, cameras, livestream, slides), Hospitality, Ushers and Kids, each with positions. Staff **schedule** people to positions on a plan. People **accept or decline** in the app or by email, and staff see gaps and declines at a glance. | Team leaders, volunteers |
| Blockout dates | Volunteers mark the dates they're away; they can't be scheduled then and show as unavailable. | Volunteers |
| Reminders | A request when scheduled, and a reminder a few days before (push and email; SMS when a provider is chosen). | Everyone scheduled |
| Matrix | One screen showing the coming weeks' plans side by side with every position, so gaps stand out. | Pastors, team leaders |
| Rehearse / Music Stand | **Rehearse** in the member app: the plan's songs with chord charts and reference audio. A **Music stand** view in the admin portal shows full-screen charts for a tablet, with next and previous controls. | Band |
| Services Live | **Live run sheet:** during the service, the current item, a countdown for its length, and what's next. Shared on screens and phones. | Producer, pastor, band |
| CCLI reporting | A list of songs used in a period with CCLI numbers, for the church's copyright report. | Administrator |

## How it fits the platform
- A new **Services** module (schema `services`).
  - **Teams are linked to ministries** (Church module), so the scoped permissions work as they do elsewhere: a worship leader manages the Worship team, and the campus pastor sees everything at the campus.
- **People:** volunteers are People records. Scheduling never creates logins; the request email lets someone accept or decline without one, using a signed link (as with event tickets).
- **Communications:** requests and reminders go through the existing notification module, with consent and quiet hours respected.
- **Livestream:** a plan can link to that Sunday's livestream, so the run sheet and the stream share one timeline.
- **Safeguarding:** under-18 volunteers can be scheduled only on teams marked as open to minors, and always with an adult on the same team.
- **Files** use the existing media storage. Chord charts are PDFs the church already owns. We don't host lyrics text unless the church confirms its CCLI licence.

## Build order (each usable on its own)
1. **Teams and scheduling** (the biggest weekly time-saver):
   - teams and positions;
   - schedule people to a plan;
   - accept or decline by app or email link;
   - blockout dates;
   - reminders;
   - the matrix;
   - "My schedule" in the member app.
2. **Service plans:** templates, the order of service with times, notes per item and per team, and printing or PDF.
3. **Song library:** arrangements and keys, chord charts and references, last-used dates, and the CCLI report.
4. **Rehearse and Music stand:** charts and audio in the app, and a full-screen stand view on a tablet.
5. **Live run sheet:** current and next item, countdown, and a link to the livestream.

## Questions for the pastor
1. **Which part first?** I suggest teams and scheduling. It saves the most phoning and WhatsApping each week.
2. **Which teams and positions** does Shapers run today, and who leads each?
3. **Does Shapers hold a CCLI licence** (church copyright and streaming)? This decides whether lyrics can be stored and shown.
4. **How do volunteers answer today?** Most will have the app; the email link covers the rest until SMS or WhatsApp is set up.
