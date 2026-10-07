# 0020. Kids check-in: parents check in, a pickup code hands children back

Status: Proposed (2026-10-07)

## Context
Parents leave their children with the kids team during the service. The team needs to know who is in each class and which children have allergies or medical needs, and must hand each child back only to the person who brought them. Children's details, and health details above all, are the most protected personal information under POPIA. A child can't consent for themselves; their parent or guardian does. The church hasn't yet chosen its classes, a label printer or who besides a parent may collect a child, so the first version works with defaults the church can change.

## Decision
- **Kids module** (schema `kids`) with classes, care notes and check-ins. A child is a People record in their parent's household; Kids stores the child's People ID and asks People through `IFamilyRecords`.
- **Parents add their children in the app.** People creates the child's record in the parent's household and campus, and records the parent's consent, as guardian, on the child's record. At the desk the kids team does the same for a visiting family, after the parent agrees, and the consent is recorded as taken by that staff member.
- **Classes by age.** The church's classes have age ranges; a child goes to the narrowest class that fits their age on the day. Four defaults are created on first start: *Little ones* (0–2), *Pre-school* (3–5), *Primary* (6–9) and *Pre-teens* (10–12).
- **Pickup codes.** Children checked in together share a random four-character code (no 0/O or 1/I/L), unique among the children still in class that day. The parent sees it in the app or on the desk screen; the label shows it. A child is handed back only when the team enters a matching code: a wrong code shows no names, and every handover is audited with who did it.
- **Care notes** (allergies, medical needs, anything else) are written by the parent or at the desk. The check-in screen shows only that a child *has* notes. Reading them needs `kids.care.view`, which is sensitive (two-step sign-in), and every read is audited.
- **Permissions:**
  - `kids.checkin`: today's classes, the desk, labels and handing back. Never the notes or the codes in lists.
  - `kids.care.view`: read care notes (sensitive).
  - `kids.manage`: set up classes.
- **Retention:** check-ins are deleted two years after the day, so there is history if a safeguarding concern is raised. Care notes are deleted once a child hasn't been checked in for two years and the notes haven't changed in that time. Both are in the POPIA export and erasure; erasing a parent removes them as the guardian on their children's check-ins.

## Consequences
- No new external service or cost. Labels print through the browser on whatever printer the church chooses.
- Not yet: other authorised collectors, a self-service kiosk, texting the code to visitors (the desk shows it, and the label carries it).
- Returning families without the app are checked in at the desk as new visitors; staff merge any duplicate records, which People already flags.
