# 0008. Append-only audit log and consent records

Status: Accepted (2026-09-28)

## Context
Church records reveal religious belief, which POPIA treats as special personal information. We must be able to show who accessed or changed a record, and on what basis we process it.

## Decision
- `platform.audit_entries` records the actor, action, entity, scope, time, IP address and request ID. A database trigger rejects UPDATE, DELETE and TRUNCATE on it, whoever connects.
- Every change to a person, household, consent record or grant is audited. Viewing a person's full record is audited as a sensitive read.
- Consent is stored as records that are only ever added to: purpose, granted or withdrawn, lawful basis, privacy-notice version and source. The current position is the latest record for each purpose.
- Medical, pastoral and giving data live in their own modules, not in People.
- Sign-in codes and expired sessions are deleted on a schedule.

## Consequences
- The audit log grows without limit, so it will eventually need archiving to cheaper storage.
- Correction and deletion requests need a documented process that respects the audit log. That comes in V1.
