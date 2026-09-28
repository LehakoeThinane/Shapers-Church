# 0005. Transactional outbox and Hangfire

Status: Accepted (2026-09-28)

## Context
Modules react to each other: a merge must re-point a login, and a donation must update giving statements. Publishing an event straight after saving can lose it on a crash, or publish it for a change that was rolled back.

## Decision
- Aggregates raise domain events. Each module decides which of them become integration events. Those are written to the module's `outbox_messages` table in the same transaction as the change.
- A background service per module claims pending rows with `FOR UPDATE SKIP LOCKED` and dispatches them in-process. An inbox table records which handler processed which event, so a retry re-runs only the handlers that failed. Delivery is at least once, and handlers are idempotent.
- **Hangfire** (PostgreSQL storage) runs recurring jobs that modules declare: clean-ups now, reminders and statements later. We didn't choose MassTransit: it pays off with a message broker and several services, and we have neither.

## Consequences
- There is no broker to run, and several API instances can process the outbox safely.
- Effects across modules are asynchronous, usually taking under a second.
