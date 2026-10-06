# 0016. Home cells and leadership by membership

Status: Accepted (2026-10-06)

## Context
Home cells are the church's small groups. Each has a leader (sometimes a co-leader) who records every meeting and prepares teaching. The pastors oversee all cells. Reports hold special personal information: who came, prayer needs, people who need a pastor's attention. The first plan gave leaders a *Cell leader* role granted at the cell's scope. Leaders change often, though, and keeping role grants in step with cell membership is easy to get wrong.

## Decision
- **Groups module** (schema `groups`) with cells, members, reports and materials. A cell belongs to a campus and carries the campus's scope.
- **Leaders are whoever the cell says they are.** A person who is a *Leader* or *Co-leader* of an active cell can manage that cell's members, reports and materials. This is checked against the membership on every request, not through a role grant, so changing a cell's leader in the admin portal is the only step needed.
- **Leaders need two-step sign-in** (the same switch as other sensitive data: `Auth:Security:RequireMfaForSensitivePermissions`). Without it, the API answers `groups.mfa_required` and the portal points them to *My security*.
- **Pastors use scoped permissions:**
  - `groups.cells.manage`: create and close cells, and choose leaders and members.
  - `groups.reports.view`: read reports and materials. It is sensitive, and every report opened is audited.

  Campus pastors hold both, so pastors see every cell in their campus.
- **Reports** have four parts:
  - attendance, with visitors;
  - topic and notes;
  - prayer and follow-ups, with an urgent flag;
  - growth and next steps.

  Submitting a report:
  - sends visitors who agreed to be contacted to People as connect cards (visitors who did not agree are kept by first name only);
  - alerts the pastors about urgent follow-ups, without names.
- **Retention:** a year after the meeting, the personal parts of a report are removed. Attendance numbers stay for trends.
- **Members** see their cell's meeting details and the materials shared with them.

## Consequences
- No role grants to keep in step with membership. A leader who is removed from a cell loses access immediately.
- A cell leader needs a staff login (with two-step sign-in) to use the admin portal. Member-app screens for leaders can come later on the same API.
- Visitors and follow-ups reach the existing People and Communications flows through integration events. Groups never writes to their tables.
