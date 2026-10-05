# 0012. Privacy rights, erasure and retention

Status: Accepted (2026-09-30)

## Context
POPIA gives people the right to see their personal information, have it corrected and have it deleted (sections 23 and 24). It requires information to be kept no longer than needed (section 14) and breaches to be reported to the Information Regulator and the people affected (section 22). Church data includes special personal information: religious belief, health details in prayer requests, and children's details. Personal data is spread across many modules, and more modules will come.

## Decision
- **One contract.** Every module that stores personal data implements `IPersonalDataSource`, which exports and erases it. The Privacy module runs every source, so download-my-data and deletion always cover the whole platform. A new module isn't finished until it implements the contract.
- **Access is self-service.** A signed-in member downloads a readable JSON file of everything held about them, straight away. Every download is audited. No request or emailed link is needed, since many members have no email address on record.
- **Correction and deletion go to the Information Officer**, with a 30-day response target and an overdue flag. Declining needs a reason, which the person sees.
- **Erasure deletes where it can and anonymises where a record must stay.**
  - The login, sessions, grants, prayer requests, notifications, phones, playback history, connect cards and household links are deleted.
  - Event bookings keep their seat and attendance counts, but lose names and answers.
  - The church record becomes an empty "Erased" shell, so references elsewhere still resolve. Old duplicates merged into it are erased too.
  - Consent records are kept as evidence. They hold no details beyond the ID.
  - Giving records will be anonymised after the five years SARS requires.
- **Retention** (approved by leadership on 2026-09-30). Nightly jobs apply it:
  - Prayer requests: 1 year.
  - Connect cards: 2 years.
  - Listening history: 1 year.
  - Notifications: 1 year.
  - Unverified connect-card guests: 90 days.
  - Audit log: 5 years.
- **The audit log stays tamper-proof.** The database refuses updates and truncation, and allows deleting only rows older than five years.
- **The privacy notice is versioned and served by the API.** Consent records store the version people agreed to. When the version changes, members are asked to read the new notice on their next launch.
- **The breach register** records each incident, the information involved, containment, and when the Regulator and the people affected were told. It flags incidents where the Regulator hasn't been told within 72 hours (the church's own target; POPIA says "as soon as reasonably possible"). An incident can't be closed until the Regulator has been told and containment is recorded.

## Consequences
- The Information Officer has one place for requests, breaches and the audit log. The church must register its Information Officer with the Regulator.
- Erasure can't be undone. The admin screen says exactly what will be removed before anyone confirms.
- The notice text lives in the code until the content system (slice G) can hold it. Changing it needs a release and a version bump.
