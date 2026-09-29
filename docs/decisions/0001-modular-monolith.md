# 0001. Modular monolith with Contracts between modules

Status: Accepted (2026-09-28)

## Context
A small team is building many products (People, Giving, Events, Check-in and more) that share one database of people. Microservices would add deployment, networking and consistency costs we don't need.

## Decision
One ASP.NET Core application. Each module has Domain, Application, Infrastructure and Api projects, plus a Contracts project that is its only public surface: integration events, query interfaces and permission keys. Each module owns a PostgreSQL schema, with no foreign keys or joins across schemas. Architecture tests fail the build if a module references another module's internals.

## Consequences
- One deployable, one database, simple local development.
- Consistency across modules is eventual (via the outbox, see 0005), so event handlers must be idempotent.
- A module could later be extracted into its own service, because its boundary is already enforced.
