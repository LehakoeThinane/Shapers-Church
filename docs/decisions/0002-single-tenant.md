# 0002. Single-tenant, organisation as scope root

Status: Accepted (2026-09-28)

## Context
The platform is for Shapers Church only. Multi-tenancy (tenant resolution, per-tenant query filters, onboarding) would add cost to every feature.

## Decision
There is exactly one Organisation. It is the root of the scope tree (`shapers`), so every scoped record already sits under it. There is no tenant resolution and no global tenant filter.

## Consequences
- Simpler code and queries now.
- Offering the platform to other churches later would need a tenant boundary. Scope paths that start at the organisation keep that door partly open.
