# Plan: admin Home, product switcher, and home cells

Status: approved (2026-10-06). Builds on the Groups module planned for V2, starting with home cells.

## 1. Admin Home and product switcher

- **Home** becomes the first page after sign-in, showing **your tasks**. Each card appears only if you have the permission behind it:
  - prayer requests to review
  - new connect cards
  - announcements waiting for a second approver
  - privacy requests near their 30-day deadline
  - this week's events
  - cell reports not yet submitted (pastors)
  - your cell's next meeting (leaders)
- **Product switcher**, grouped like Planning Center:

| Group | Products (✅ built · 🆕 in this plan · ⏳ coming soon) |
|---|---|
| People & communication | ✅ People · 🆕 **Cells** · ✅ Announcements · ✅ Prayer |
| Worship & teams | ⏳ Services · ⏳ Music stand |
| Donations | ⏳ Giving |
| Events | ✅ Registrations (events) · ⏳ Calendar · 🟡 Check-ins (event door now, kids later) |
| Mobile app & website | ✅ Publishing (pages, posts, sermons, livestream) · ✅ Live chat |
| System tools | ✅ Roles & access · ✅ Privacy & breaches · ✅ Audit log · ✅ Security |

- Each product has its own menu instead of one list of 16 links. Products without access are hidden; *coming soon* ones are shown greyed out.

## 2. Home cells

### Who sees what
Access uses the existing scoped permissions (Role → Permissions → Scope). No new mechanism is needed.

```
Church (global)        Church administrator: everything
 └─ Campus (Rivonia)   Campus pastor: every cell, every report and material (decision: pastors see all cells)
     └─ Cell           Cell leader / co-leader: their cell only
         └─ Members    Their own cell's meeting details (member app)
```

| Permission | Meaning | Given to |
|---|---|---|
| `groups.cells.manage` | Create and close cells, assign leaders and members | Campus pastor, Campus administrator, Church administrator |
| `groups.cells.lead` | Manage their cell's members, record meetings, write reports and materials | **Cell leader** role, granted **at the cell's scope** |
| `groups.reports.view` *(sensitive)* | Read reports and materials | Campus pastor (campus scope), Church administrator. **Every read is audited** |

- Assigning a leader in the admin portal grants the *Cell leader* role at that cell's scope. If the leader has no staff login yet, it sends them a set-password email (the existing staff invite flow). Removing them as leader revokes the grant.
- Reports can contain special personal information (faith, health, struggles). Reading them is limited and audited, and is kept separate from the confidential **Pastoral Care** module.

### Data (new `groups` schema, Groups module)
- **Cell:** name, campus, meeting day, time and area, address (shown to members only), status (active or closed), scope path (`…campus.cells.<id>`).
- **Membership:** person, role (leader, co-leader or member), joined and left dates.
- **Meeting report** (decision: all four parts), with draft and submitted states:
  1. **Attendance:** members present; visitors' names and contacts. Each visitor becomes a **connect card**, so it shows up in the existing follow-up queue.
  2. **Topic and notes:** what was taught (optionally linked to one of the leader's materials), how it went, highlights and testimonies.
  3. **Prayer and follow-ups:** prayer needs; people to follow up, each with a note and an **urgent flag** that alerts the pastors.
  4. **Growth and next steps:** who is ready for baptism, the Growth Track, serving or leadership; the cell's readiness to multiply.
- **Material** (decision: leaders write their own): title, series or topic, body text, attachments (PDF or images, using the existing file storage), date, and visibility: *leaders and pastors* or *also members*.

### Screens
- **Leader, admin portal "My cell":**
  - members (add or remove)
  - **record this week's meeting**, as a report form that works well on a phone
  - **materials** (write or upload; share with members or keep private)
  - report history
- **Pastor, admin portal "Cells":**
  - an overview of every cell: last report, **missing reports this week**, attendance trend, urgent flags
  - a report reader
  - a materials library across all cells
  - manage cells and leaders
- **Member, app (Community tab) "My cell":** next meeting, leader contact, materials shared with members.

### Member sign-in
- Add **email-code sign-in** for members, next to SMS. It uses the same 6-digit code, limits and lockouts.
- Add a **Resend** email sender (`Email:Provider = Resend`) next to Azure. Resend sends email only; SMS still needs an SMS provider later.

## 3. Build order (each step usable and tested on its own)
1. Admin Home, tasks and product switcher, plus per-product menus (admin portal, with a small summary endpoint per module).
2. Groups module backend: cells, memberships, scoped leader grants, reports, materials, audit on reads, visitors as connect cards, urgent-flag alerts. Domain, permission and integration tests.
3. Admin: the leader's "My cell" and the pastor's "Cells" overview.
4. Member app "My cell", member email-code sign-in, and the Resend sender.
5. ADR 0016 (cells and scoped leadership), README, docs.

## 4. Decisions
- **Cell reports are kept for 1 year** (POPIA retention), then the notes, prayer needs and follow-ups are deleted. Anonymous attendance numbers are kept for trends.
- **Cell leaders need two-step sign-in** (authenticator app), because reports are sensitive. It's the same rule that already applies to people's details.
- **Resend** sends email, with the key kept in server settings and Key Vault, never in the repo. Until `shaperschurch.com` is verified in Resend, its test sender only delivers to the account owner's address.
