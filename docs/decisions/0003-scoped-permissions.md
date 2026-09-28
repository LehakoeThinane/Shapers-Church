# 0003. Permissions granted at scopes; paths as text

Status: Accepted (2026-09-28)

## Context
Access differs by campus, ministry and (later) group. Checks like `if (role == "Pastor")` spread through the code and can't express "Kids ministry leader at Rivonia".

## Decision
- Modules declare **permissions** in code (`people.profiles.view`). Sensitive ones are flagged.
- A **role** is a named set of permissions. Built-in roles are synced from code at startup; staff may create custom roles.
- A **grant** gives a user a role at a **scope**. Scopes are dot-separated paths: `shapers`, `shapers.campus_rivonia`, `shapers.campus_rivonia.ministry_kids`.
- A grant covers a record when the grant's path is the record's path or one of its ancestors. Permissions never flow upwards.
- Code asks `IAuthorizer` for a permission at a scope. List queries filter by the user's allowed scopes.
- Nobody can grant a permission they don't hold at the target scope.
- Paths are stored as text and matched by prefix (`scope = p OR scope LIKE p || '.%'`) with a `text_pattern_ops` index, rather than the `ltree` extension. The behaviour is the same, it needs no extension, and paths stay plain strings in code.
- Personal access (your own profile) is an ownership rule, not a scope.

## Consequences
- Out-of-scope records return 404, so staff can't probe for who attends.
- Scope slugs are fixed at creation, so renaming a campus doesn't move its records.
- Moving a ministry to another campus would need a path migration event. That isn't needed yet.
